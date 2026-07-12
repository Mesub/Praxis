using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.Diagnostics;

/// **What should PRAXIS ask this person next?**
///
/// Everything the assessment engine believes converges on this one decision, and the two refusals
/// that shape it both live here:
///
/// **1. It is not trying to score you. It is trying to find where you break** (I-6). A standard CAT
/// maximises information about θ — it wants your ability to three decimals and will happily spend
/// forty items getting there. This one buys something cheaper and more useful: *does this learner
/// hold this concept*, and *where exactly does it fall apart*. So an item that resolves an **open
/// misconception** can beat a slightly more informative one that does not.
///
/// **2. `Adaptation` gets the last word, and it can say stop.** Left alone, an engine that hunts
/// failure will hand a struggling learner harder and harder items — **not out of malice but out of
/// arithmetic**, because every wrong answer is informative. That is a machine correctly maximising
/// the wrong thing at a human being. So before anything is chosen, PRAXIS asks whether it should be
/// asking at all.
public static class NextItem
{
    public sealed record Query(Guid SessionId, Guid ConceptId);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.SessionId).NotEmpty();
            RuleFor(q => q.ConceptId).NotEmpty();
        }
    }

    public sealed class Handler
    {
        private readonly AssessmentDbContext _db;
        private readonly IUserContext _user;

        public Handler(AssessmentDbContext db, IUserContext user) => (_db, _user) = (db, user);

        public async Task<Result<NextItemResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var ability = await _db.Abilities.AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.LearnerId == learner && a.ConceptId == query.ConceptId, ct);

            // **Starts from the Twin, never from zero** (I-1). A learner PRAXIS has never assessed on
            // this concept still is not a stranger — but until there is an estimate, `Unknown` says
            // so honestly rather than assuming they are average.
            var current = ability?.Current ?? Ability.Unknown;

            // ── Should we be asking at all? ──────────────────────────────────
            var recent = await RecentAttempts(learner, query.ConceptId, ct);

            var adjustment = Adaptation.Consider(current, recent);

            if (adjustment.ShouldStopAssessing)
            {
                // **We do not keep hammering someone who is drowning**, and *tiredness is not
                // ignorance*. This is a 200, not an error: nothing has gone wrong, and the learner is
                // not being told they failed. PRAXIS is telling them it has stopped asking.
                return Result<NextItemResponse>.Success(
                    NextItemResponse.Stop(adjustment.Says, adjustment.Ability));
            }

            // ── What is still open about this learner? ───────────────────────
            var open = await OpenMisconceptions(learner, query.ConceptId, ct);

            var seen = await _db.Responses.AsNoTracking()
                .Where(r => r.LearnerId == learner && r.ConceptId == query.ConceptId)
                .Select(r => r.ItemId)
                .Distinct()
                .ToListAsync(ct);

            var pool = await _db.Items.AsNoTracking()
                .Where(i => i.ConceptId == query.ConceptId && !seen.Contains(i.Id))
                .ToListAsync(ct);

            var candidates = pool.Select(ToCandidate).ToList();

            var context = new SelectionContext(
                Ability: current,
                HoldThreshold: HoldThreshold,
                OpenMisconceptions: open,
                Fatigue: Fatigue(recent));

            var chosen = ItemSelection.Choose(candidates, context);

            if (!chosen.IsSuccess)
            {
                // **PRAXIS will not invent an item to keep the session moving.** A made-up item
                // produces made-up evidence, and made-up evidence about a person's mind is the one
                // thing this system exists to refuse.
                return chosen.CarryFailure<NextItemResponse>();
            }

            var item = pool.Single(i => i.Id.ToString() == chosen.Value.Id);

            return Result<NextItemResponse>.Success(NextItemResponse.Ask(item, current));
        }

        /// The last few things they did, for `Adaptation` — which reads accuracy *and latency*,
        /// because **a tired learner looks exactly like a struggling one** and mistaking the two
        /// means tearing up somebody's plan because they had a long day.
        private async Task<List<Attempt>> RecentAttempts(Guid learner, Guid concept, CancellationToken ct)
        {
            var rows = await _db.Responses.AsNoTracking()
                .Where(r => r.LearnerId == learner && r.ConceptId == concept)
                .OrderByDescending(r => r.OccurredAt)
                .Take(RecentWindow)
                .ToListAsync(ct);

            var ids = rows.Select(r => r.ItemId).Distinct().ToList();

            var difficulties = await _db.Items.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Difficulty, ct);

            return rows
                .Where(r => difficulties.ContainsKey(r.ItemId))
                .OrderBy(r => r.OccurredAt)
                .Select(r => new Attempt(
                    r.Correct,
                    difficulties[r.ItemId],
                    r.Seconds,
                    WasConfident: r.StatedConfidence >= Diagnosis.ConfidentAt))
                .ToList();
        }

        /// Misconception key → how likely PRAXIS thinks this learner holds it.
        ///
        /// **An uncertain belief is worth probing; a settled one is not.** A `Suspected` belief is the
        /// interesting case — PRAXIS genuinely does not know, and an item that would settle it is
        /// worth more than an item that merely measures θ more precisely.
        private async Task<Dictionary<string, double>> OpenMisconceptions(
            Guid learner, Guid concept, CancellationToken ct)
        {
            var held = await _db.Misconceptions.AsNoTracking()
                .Where(m => m.LearnerId == learner
                            && m.ConceptId == concept
                            && m.Key != null
                            && m.Status != MisconceptionStatus.Eradicated)
                .ToListAsync(ct);

            return held.ToDictionary(
                m => m.Key!,
                m => m.Status switch
                {
                    // **Maximum uncertainty is maximum value.** One hit and PRAXIS does not know —
                    // and not knowing is exactly what makes the next item worth asking.
                    MisconceptionStatus.Suspected => 0.50,

                    MisconceptionStatus.Confirmed => 0.85,
                    MisconceptionStatus.Persistent => 0.90,
                    MisconceptionStatus.Challenged => 0.40,
                    MisconceptionStatus.Remediated => 0.20,
                    _ => 0.10,
                });
        }

        private static Candidate ToCandidate(BankedItem item)
            => new(
                Id: item.Id.ToString(),
                A: item.Discrimination,
                B: item.Difficulty,
                EvidenceType: item.EvidenceType,
                CostMinutes: CostOf(item.EvidenceType),
                Diagnoses: item.TargetsMisconception is { } key
                    ? new HashSet<string> { key }
                    : new HashSet<string>(),
                Exposure: Exposure(item),
                C: item.Guessing);

        /// **A BUILD costs twenty minutes; an APPLY costs forty seconds.** An engine that ignores that
        /// will fill a fifteen-minute session with one item and call it efficient.
        private static double CostOf(EvidenceType type) => type switch
        {
            EvidenceType.Recall => 0.3,
            EvidenceType.Apply => 0.7,
            EvidenceType.Critique => 3.0,
            EvidenceType.Explain => 5.0,
            EvidenceType.Predict => 2.0,
            EvidenceType.Build => 20.0,
            _ => 1.0,
        };

        /// Sympson–Hetter, crudely: **an item everybody sees is an item everybody has heard about.**
        private static double Exposure(BankedItem item)
            => Math.Min(1.0, item.TimesServed / (double)ExposureCeiling);

        /// Rising fatigue penalises expensive items. It is **not** a judgement about the learner —
        /// it is a fact about how long they have been going.
        private static double Fatigue(List<Attempt> recent)
            => Math.Min(1.0, recent.Count / (double)FatigueAfter);
    }

    /// How many recent attempts `Adaptation` is shown.
    ///
    /// **This was 5, and 5 silently broke the most important refusal in the file.** Drowning needs
    /// the last two attempts; **tiredness needs at least six**, because it compares the first half of
    /// a session against the second half and three points do not have a shape. A window of five can
    /// never contain six, so `IsTiring` could not fire — *"tiredness is not ignorance"* was dead code,
    /// and a tired learner would have been quietly de-escalated and had prerequisites injected into
    /// their plan **because they had a long day**.
    ///
    /// Nothing failed. No test went red. The check was simply never reached, which is the worst way
    /// for a guarantee to die.
    public const int RecentWindow = 12;

    /// Served this many times, an item is treated as fully exposed.
    public const int ExposureCeiling = 500;

    /// Items into a sitting before PRAXIS starts treating expensive ones as expensive.
    public const int FatigueAfter = 12;

    /// The concept's hold threshold in logits. **This is the question the whole session is trying to
    /// answer** — not *"how good are you"* but *"do you hold this"*.
    ///
    /// Hard-coded for now: it belongs to the concept's Proof-of-Mastery contract, which lives in the
    /// Graph, and the seam between the two modules is drawn at M13 with the real concept pack. A
    /// wrong constant here makes the hunt slightly less well aimed; **it cannot manufacture mastery**,
    /// because nothing in this file writes evidence.
    public const double HoldThreshold = 0.0;
}
