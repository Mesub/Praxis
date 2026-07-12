using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;

namespace Praxis.Modules.Assessment.Projections;

/// **What PRAXIS believes about a learner is derived, not decided.**
///
/// There is no endpoint that sets somebody's ability. No support tool, no admin override, no
/// migration that nudges a learner up after a bad week and a complaint. The only way to change what
/// PRAXIS thinks of you is to **do something and have it written down**, and then let the estimator
/// draw its own conclusion from the whole history.
///
/// That is inconvenient in exactly the way that stops a belief about a person's mind from being
/// edited by somebody in a hurry.
///
/// ## Why it re-runs the whole history instead of nudging the last number
///
/// The obvious implementation is incremental: take the current θ, apply this one response, save. It
/// is faster, and it is wrong — because **an incremental update cannot be replayed**. Two learners
/// with identical histories would end up with different numbers depending on the order the server
/// happened to process things in, and at that point the estimate is not a projection, it is an
/// accident that got persisted.
///
/// So the estimator is handed **every** response this learner has ever given on this concept and
/// recomputes the posterior from the wide prior each time (EAP over a 161-point quadrature — M7.2).
/// Replay the log twice, get the same number twice. That is the contract, and it is worth the
/// arithmetic.
///
/// ## And the difficulty is read from the bank, not from the event
///
/// An event that carried its own difficulty would freeze what was believed on the day it was written.
/// Item calibration *improves* (M7.2), so a replay would faithfully reproduce yesterday's mistakes
/// instead of today's best estimate. The event says **what happened**; the bank says **what it was
/// worth**. Same rule as the Answer Budget (M6.1), and for the same reason: a writer that can name
/// its own price will eventually name a generous one.
public sealed class AbilityProjection : IProjection
{
    private readonly AssessmentDbContext _db;

    public AbilityProjection(AssessmentDbContext db) => _db = db;

    public string Name => "assessment.abilities";

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.AnswerGiven,
    };

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ConceptId is not { } conceptId)
        {
            // An answer about nothing. The writer is broken; the log is append-only so we cannot fix
            // it — but we can decline to draw a conclusion about a human being from it.
            return;
        }

        var answer = EventCatalogue.Deserialize<AnswerGiven>(e);

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == answer.ItemId, ct);

        if (item is null)
        {
            // An answer to an item that is not in the bank. **Do not guess its difficulty.** An item
            // assumed to be average drags every estimate toward the middle, and the learner would
            // never find out why PRAXIS had quietly decided they were unremarkable.
            return;
        }

        // **Idempotent.** A redelivered event must not score a learner twice for one answer.
        if (await _db.Responses.AnyAsync(r => r.EventId == e.Id, ct))
        {
            return;
        }

        item.Observe(answer.Correct);

        _db.Responses.Add(RecordedResponse.From(
            e.Id,
            e.LearnerId,
            conceptId,
            answer.SessionId,
            answer.ItemId,
            answer.Correct,
            answer.StatedConfidence,
            answer.Seconds,
            e.OccurredAt));

        var estimate = await _db.Abilities
            .FirstOrDefaultAsync(a => a.LearnerId == e.LearnerId && a.ConceptId == conceptId, ct);

        if (estimate is null)
        {
            estimate = AbilityEstimate.Unknown(e.LearnerId, conceptId);

            _db.Abilities.Add(estimate);
        }

        // ── Recompute from the whole history, every time ─────────────────────
        //
        // EAP is a product of per-response likelihoods, so the order does not matter — but the
        // *completeness* does. Every answer this learner has ever given on this concept goes back
        // through the estimator, against the item parameters **as they stand today**.
        var responses = await HistoryFor(e.LearnerId, conceptId, ct);

        responses.Add(item.AsResponse(answer.Correct));

        estimate.Revise(Irt.Update(Ability.Unknown, responses), responses.Count);

        await _db.SaveChangesAsync(ct);
    }

    /// Every response this learner has already given on this concept, scored against the item
    /// parameters **as they stand now**.
    private async Task<List<Response>> HistoryFor(Guid learner, Guid concept, CancellationToken ct)
    {
        var history = await _db.Responses.AsNoTracking()
            .Where(r => r.LearnerId == learner && r.ConceptId == concept)
            .Select(r => new { r.ItemId, r.Correct })
            .ToListAsync(ct);

        var ids = history.Select(h => h.ItemId).Distinct().ToList();

        var items = await _db.Items.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        return history
            .Where(h => items.ContainsKey(h.ItemId))
            .Select(h => items[h.ItemId].AsResponse(h.Correct))
            .ToList();
    }

    /// A rebuild throws everything away and recomputes it from history. **If this ever stops working,
    /// some belief about a learner is being kept somewhere that history cannot audit** — and that is
    /// the one thing the kernel does not permit.
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _db.Responses.ExecuteDeleteAsync(ct);
        await _db.Abilities.ExecuteDeleteAsync(ct);

        // The exposure counts are derived too. Leaving them behind would make a rebuilt bank look
        // twice as over-exposed as it is, and the selector would start avoiding perfectly good items.
        await _db.Items.ExecuteUpdateAsync(
            s => s.SetProperty(i => i.TimesServed, 0).SetProperty(i => i.TimesCorrect, 0),
            ct);
    }
}
