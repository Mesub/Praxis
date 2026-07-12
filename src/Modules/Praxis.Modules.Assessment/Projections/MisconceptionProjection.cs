using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;

namespace Praxis.Modules.Assessment.Projections;

/// **The ledger of what one learner wrongly believes — and it can only be written by things that
/// happened.**
///
/// There is no `MarkAsFixed` here, no `Override`, no admin route and no support tool, and that is not
/// an oversight to be filled in later. **The moment somebody's opinion can close a misconception, the
/// thing that eventually closes it will be fatigue** — a long queue, a release deadline, a learner
/// who complained loudly enough. So the only inputs are events, and the only verdict is arithmetic
/// over receipts (M7.5).
///
/// ## The two directions
///
/// A `MisconceptionDetected` is a **hit**: one is a suspicion, two independent ones is a diagnosis,
/// and a hit against a belief PRAXIS thought was dead sends it **straight back to `Confirmed`** —
/// not back to the bottom of the ladder. It is the same old belief and PRAXIS already knows its name.
///
/// A clean `AnswerGiven` on an item whose distractors were built to catch this belief is a **receipt**
/// — and enough clean answers on a mere *suspicion* dismisses it entirely, because **a suspicion
/// PRAXIS never lets go of is a suspicion that becomes a fact by attrition.**
///
/// ## What "clean" is not
///
/// It is not "got a question right about this concept". A learner can get a general item right while
/// carrying the wrong belief perfectly intact — they simply did not walk into the trap this time. So
/// only an item that **specifically targets this belief** (`BankedItem.TargetsMisconception`) counts
/// as evidence about it, in either direction. That distinction is the difference between a
/// misconception engine and a horoscope.
public sealed class MisconceptionProjection : IProjection
{
    private readonly AssessmentDbContext _db;

    public MisconceptionProjection(AssessmentDbContext db) => _db = db;

    public string Name => "assessment.misconceptions";

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.MisconceptionDetected,
        LearningEventType.AnswerGiven,
    };

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ConceptId is not { } conceptId)
        {
            return;
        }

        switch (e.Type)
        {
            case LearningEventType.MisconceptionDetected:
                await OnHit(e, conceptId, ct);
                break;

            case LearningEventType.AnswerGiven:
                await OnAnswer(e, conceptId, ct);
                break;

            default:
                return;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// **They fell for it again.**
    private async Task OnHit(LearningEvent e, Guid conceptId, CancellationToken ct)
    {
        var detected = EventCatalogue.Deserialize<MisconceptionDetected>(e);

        var existing = await Find(e.LearnerId, conceptId, detected.Name, ct);

        if (existing is null)
        {
            // **Learner-local until proven otherwise.** PRAXIS hunts this belief for *this person*
            // straight away — we can target a belief we have not named globally — but it does not
            // become a fact about the world until 25 others hold it and a human has agreed (M7.5).
            _db.Misconceptions.Add(LearnerMisconception.Suspect(
                e.LearnerId, conceptId, detected.Name, detected.Evidence, e.OccurredAt));

            return;
        }

        existing.Hit(e.OccurredAt);
    }

    /// An answer on an item built to catch a specific wrong belief. **Only these count.**
    private async Task OnAnswer(LearningEvent e, Guid conceptId, CancellationToken ct)
    {
        var answer = EventCatalogue.Deserialize<AnswerGiven>(e);

        var item = await _db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == answer.ItemId, ct);

        if (item?.TargetsMisconception is not { } targeted)
        {
            // A general item. It says nothing about this belief in **either** direction — getting it
            // right may just mean they did not walk into the trap this time.
            return;
        }

        var mc = await Find(e.LearnerId, conceptId, targeted, ct);

        if (mc is null || !answer.Correct)
        {
            // A *wrong* answer on a targeted item is a hit — but the hit arrives as its own
            // `MisconceptionDetected` event, written by the grader that actually diagnosed it. This
            // projection does not diagnose; it records. **A projection that draws its own conclusions
            // is a second grader nobody reviewed.**
            return;
        }

        // ── A clean answer on the discriminating dimension ───────────────────
        //
        // Against a mere **suspicion**, this is what lets it go. Three of them and PRAXIS drops it,
        // because **a suspicion it never lets go of is a suspicion that becomes a fact by
        // attrition** — and an engine that keeps every hunch open until it is eventually right is
        // not being careful, it is being unfalsifiable.
        mc.CleanAnswer();

        // ── And against a confirmed belief, a receipt ────────────────────────
        //
        // The row knows which session it last earned a receipt in, so *"one good day is not a
        // repair"* is a fact it can check for itself — rather than a lookup into another
        // projection's table, which would make this a race rather than a projection.
        mc.Earn(
            e.OccurredAt,
            answer.SessionId,
            targetedItemPassed: true,
            critiquedIt: answer.EvidenceType == Shared.EvidenceType.Critique,
            differentModality: answer.EvidenceType != Shared.EvidenceType.Recall);

        if (mc.IsDismissed)
        {
            // It was only ever a suspicion, and it did not survive contact with the evidence. **Let
            // it go.** A tool that keeps every suspicion open until it is eventually right is not
            // being careful, it is being unfalsifiable.
            _db.Misconceptions.Remove(mc);
        }
    }

    private Task<LearnerMisconception?> Find(Guid learner, Guid concept, string key, CancellationToken ct)
        => _db.Misconceptions.FirstOrDefaultAsync(
            m => m.LearnerId == learner && m.ConceptId == concept && m.Key == key, ct);

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _db.Misconceptions.ExecuteDeleteAsync(ct);
    }
}
