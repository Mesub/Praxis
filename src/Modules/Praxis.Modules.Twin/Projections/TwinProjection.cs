using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Projections;

/// **The Twin.**
///
/// Every pure function built in M3.2–M3.6 — the belief update, the forgetting curve, calibration,
/// the Evidence Ceiling, the misconception set — meets the real event log here.
///
/// It is a *projection*: everything it writes is derived, and a rebuild throws it all away and
/// recomputes it from history (M1.6). So it must be a **pure function of the events it has seen**.
/// It reads no clock, calls no model, and rolls no dice. Timestamps come from the events
/// themselves, because that is when things happened in the learner's world — not when we got round
/// to processing them.
///
/// If this file ever reads `DateTime.UtcNow`, the Twin stops being reproducible and the Ledger
/// stops being verifiable. That is the whole game.
public sealed class TwinProjection : IProjection
{
    private readonly TwinDbContext _db;
    private readonly IMasteryContracts _contracts;

    public TwinProjection(TwinDbContext db, IMasteryContracts contracts)
        => (_db, _contracts) = (db, contracts);

    public string Name => "twin.concept-states";

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.AnswerGiven,
        LearningEventType.RepAnswered,
        LearningEventType.EvidenceAccepted,
        LearningEventType.MisconceptionDetected,
        LearningEventType.PredictionResolved,
    };

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ConceptId is not { } conceptId)
        {
            return;   // session lifecycle and the like — nothing to believe about a concept
        }

        switch (e.Type)
        {
            case LearningEventType.AnswerGiven:
                await OnAnswerAsync(e, conceptId, EventCatalogue.Deserialize<AnswerGiven>(e), ct);
                break;

            case LearningEventType.RepAnswered:
                await OnRepAsync(e, conceptId, EventCatalogue.Deserialize<RepAnswered>(e), ct);
                break;

            case LearningEventType.EvidenceAccepted:
                await OnEvidenceAsync(e, conceptId, EventCatalogue.Deserialize<EvidenceAccepted>(e), ct);
                break;

            case LearningEventType.MisconceptionDetected:
                await OnMisconceptionAsync(e, conceptId, EventCatalogue.Deserialize<MisconceptionDetected>(e), ct);
                break;

            case LearningEventType.PredictionResolved:
                await OnPredictionAsync(e, conceptId, EventCatalogue.Deserialize<PredictionResolved>(e), ct);
                break;

            default:
                break;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── A graded answer ──────────────────────────────────────────────────────

    private async Task OnAnswerAsync(LearningEvent e, Guid conceptId, AnswerGiven payload, CancellationToken ct)
    {
        var state = await StateFor(e.LearnerId, conceptId, ct);
        var contract = await _contracts.ForConceptAsync(conceptId, ct);

        var difficultyPrior = contract is null ? 0.0 : 0.0;   // graph prior arrives with the contract (M4)
        var bkt = BktParameters.ForDifficulty(difficultyPrior);

        // The belief, updated by Bayes. Decayed first, because the learner arrives at this answer
        // having forgotten some of what they knew — and the answer is evidence about *now*.
        var decayed = Decayed(state, e.OccurredAt);
        var pKnows = Bkt.Observe(decayed, payload.Correct, bkt);

        var calibration = Calibration.Observe(
            CalibrationOf(state), payload.StatedConfidence, payload.Correct);

        var difficulty = Fsrs.NextDifficulty(state.Difficulty, Grading.Of(payload.Correct));

        Write(state, pKnows, calibration, difficulty, state.Stability, state.LastEvidenceAt, e.OccurredAt);
    }

    // ── A rep ────────────────────────────────────────────────────────────────

    private async Task OnRepAsync(LearningEvent e, Guid conceptId, RepAnswered payload, CancellationToken ct)
    {
        var state = await StateFor(e.LearnerId, conceptId, ct);

        var elapsed = DaysSince(state.LastEvidenceAt, e.OccurredAt);
        var retrievability = Fsrs.Retrievability(state.Stability, elapsed);

        // The spacing effect: how hard it *was* to recall determines what recalling it was worth.
        // E7 owns this arithmetic; the Twin only stores what comes out of it.
        var stability = Fsrs
            .Next(new MemoryState(state.Stability, state.Difficulty), Grading.Of(payload.Correct), elapsed)
            .Stability;

        var bkt = BktParameters.ForDifficulty(0.0);
        var pKnows = Bkt.Observe(Decayed(state, e.OccurredAt), payload.Correct, bkt);

        var calibration = Calibration.Observe(
            CalibrationOf(state), payload.StatedConfidence, payload.Correct);

        var difficulty = Fsrs.NextDifficulty(state.Difficulty, Grading.Of(payload.Correct));

        Write(state, pKnows, calibration, difficulty, stability, e.OccurredAt, e.OccurredAt);
    }

    // ── Evidence that was accepted — the only thing that moves mastery ───────

    private async Task OnEvidenceAsync(LearningEvent e, Guid conceptId, EvidenceAccepted payload, CancellationToken ct)
    {
        // Idempotent: replaying an event already applied must not count it twice, or a rebuild
        // would inflate every learner in the system.
        var already = await _db.Evidence
            .AnyAsync(x => x.EvidenceId == payload.EvidenceId, ct);

        if (!already)
        {
            _db.Evidence.Add(EvidenceItem.Of(
                e.LearnerId, conceptId, payload.EvidenceId,
                payload.Type, payload.Score, payload.Assurance, e.OccurredAt));

            await _db.SaveChangesAsync(ct);
        }

        var contract = await _contracts.ForConceptAsync(conceptId, ct);

        if (contract is null)
        {
            // The pack loader refuses to publish a concept without a contract, so this means the
            // graph was written to outside the gate. Refuse to guess what would prove it.
            throw new InvalidOperationException(
                $"Concept {conceptId} has no PoM contract, but evidence was accepted for it. "
                + "Nothing can be believed about a concept whose standard of proof is unknown — "
                + "and inventing one here would be the exact dishonesty the Evidence Ceiling exists "
                + "to prevent.");
        }

        var state = await StateFor(e.LearnerId, conceptId, ct);

        var evidence = await _db.Evidence
            .Where(x => x.LearnerId == e.LearnerId && x.ConceptId == conceptId)
            .OrderBy(x => x.OccurredAt)
            .Select(x => new AcceptedEvidenceRecord(x.Type, x.Score, x.Assurance))
            .ToListAsync(ct);

        // THE CEILING. No quantity of model-graded evidence gets a learner over the line.
        var assessment = MasteryCredit.Compute(contract, evidence);

        // Belief tracks the evidence, and is suppressed by any wrong model they are still carrying.
        var penalty = await MisconceptionPenaltyAsync(e.LearnerId, conceptId, ct);
        var pKnows = Math.Clamp(assessment.MasteryCredit - penalty, 0.0, 1.0);

        // A concept only becomes durable once it has been proved. Before that there is nothing to
        // schedule a rep for.
        var stability = state.Stability > 0
            ? state.Stability
            : Fsrs.InitialStability(Grade.Good);

        state.Set(
            pKnows: pKnows,
            masteryCredit: assessment.MasteryCredit,
            isHeld: assessment.IsHeld,
            stability: stability,
            difficulty: state.Difficulty,
            lastEvidenceAt: e.OccurredAt,
            nextDueAt: e.OccurredAt.AddDays(Fsrs.Interval(stability, TargetRetention)),
            meanStatedConfidence: state.MeanStatedConfidence,
            meanCorrectness: state.MeanCorrectness,
            brierScore: state.BrierScore,
            calibrationSamples: state.CalibrationSamples,
            assurance: assessment.Assurance,
            evidenceCount: evidence.Count);
    }

    // ── A named wrong belief ─────────────────────────────────────────────────

    private async Task OnMisconceptionAsync(
        LearningEvent e, Guid conceptId, MisconceptionDetected payload, CancellationToken ct)
    {
        var key = payload.Name.Trim().ToLowerInvariant();

        var existing = await _db.Misconceptions
            .FirstOrDefaultAsync(m => m.LearnerId == e.LearnerId
                                      && m.ConceptId == conceptId
                                      && m.Key == key, ct);

        if (existing is null)
        {
            _db.Misconceptions.Add(Misconception.Observed(
                e.LearnerId, conceptId, key, payload.Evidence, e.OccurredAt));
        }
        else
        {
            existing.ObserveAgain(e.OccurredAt);
        }

        await _db.SaveChangesAsync(ct);

        // Carrying a confidently-held wrong model makes you worse off than knowing you do not know,
        // so belief in the concept is suppressed — however well they score around it.
        var state = await StateFor(e.LearnerId, conceptId, ct);
        var penalty = await MisconceptionPenaltyAsync(e.LearnerId, conceptId, ct);

        state.Set(
            pKnows: Math.Clamp(state.MasteryCredit - penalty, 0.0, 1.0),
            masteryCredit: state.MasteryCredit,
            isHeld: state.IsHeld,
            stability: state.Stability,
            difficulty: state.Difficulty,
            lastEvidenceAt: state.LastEvidenceAt,
            nextDueAt: state.NextDueAt,
            meanStatedConfidence: state.MeanStatedConfidence,
            meanCorrectness: state.MeanCorrectness,
            brierScore: state.BrierScore,
            calibrationSamples: state.CalibrationSamples,
            assurance: state.Assurance,
            evidenceCount: state.EvidenceCount);
    }

    // ── Reality settled a prediction ─────────────────────────────────────────

    private async Task OnPredictionAsync(
        LearningEvent e, Guid conceptId, PredictionResolved payload, CancellationToken ct)
    {
        // The world graded them. That is the least fakeable evidence there is, and it lands in
        // calibration the same way everything else does — a prediction is a stated confidence that
        // reality then judged.
        var state = await StateFor(e.LearnerId, conceptId, ct);

        // Brier came from the Reality Engine, which knows the stated confidence. Recover it: a
        // Brier of b with outcome o means |confidence - o| = sqrt(b).
        var error = Math.Sqrt(Math.Clamp(payload.BrierScore, 0.0, 1.0));
        var confidence = payload.Correct ? 1.0 - error : error;

        var calibration = Calibration.Observe(CalibrationOf(state), confidence, payload.Correct);

        var bkt = BktParameters.ForDifficulty(0.0);
        var pKnows = Bkt.Observe(Decayed(state, e.OccurredAt), payload.Correct, bkt);

        Write(state, pKnows, calibration, state.Difficulty, state.Stability, state.LastEvidenceAt, e.OccurredAt);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static void Write(
        ConceptState state,
        double pKnows,
        CalibrationState calibration,
        double difficulty,
        double stability,
        DateTime? lastEvidenceAt,
        DateTime occurredAt)
    {
        var due = stability > 0
            ? occurredAt.AddDays(Fsrs.Interval(stability, TargetRetention))
            : state.NextDueAt;

        state.Set(
            pKnows: pKnows,
            masteryCredit: state.MasteryCredit,
            isHeld: state.IsHeld,
            stability: stability,
            difficulty: difficulty,
            lastEvidenceAt: lastEvidenceAt,
            nextDueAt: due,
            meanStatedConfidence: calibration.MeanStatedConfidence,
            meanCorrectness: calibration.MeanCorrectness,
            brierScore: calibration.BrierScore,
            calibrationSamples: calibration.Samples,
            assurance: state.Assurance,
            evidenceCount: state.EvidenceCount);
    }

    private async Task<ConceptState> StateFor(Guid learnerId, Guid conceptId, CancellationToken ct)
    {
        var existing = await _db.States
            .FirstOrDefaultAsync(s => s.LearnerId == learnerId && s.ConceptId == conceptId, ct);

        if (existing is not null)
        {
            return existing;
        }

        var fresh = ConceptState.Unseen(learnerId, conceptId);
        _db.States.Add(fresh);

        return fresh;
    }

    private async Task<double> MisconceptionPenaltyAsync(Guid learnerId, Guid conceptId, CancellationToken ct)
    {
        var active = await _db.Misconceptions
            .Where(m => m.LearnerId == learnerId && m.ConceptId == conceptId && m.IsActive)
            .Select(m => m.TimesObserved)
            .ToListAsync(ct);

        if (active.Count == 0)
        {
            return 0.0;
        }

        // Several wrong models about one concept are worse than one, but the penalty does not
        // compound without limit — past a point, the learner simply does not hold the concept.
        var worst = active.Max();
        var severity = Math.Min(1.0, worst / 3.0);

        return MisconceptionSet.MaxPenalty * severity;
    }

    private static double Decayed(ConceptState state, DateTime at)
        => Fsrs.DecayBelief(
            state.PKnows,
            ConceptState.PriorPKnows,
            state.Stability,
            DaysSince(state.LastEvidenceAt, at));

    private static double DaysSince(DateTime? from, DateTime to)
        => from is null ? 0.0 : Math.Max(0.0, (to - from.Value).TotalDays);

    private static CalibrationState CalibrationOf(ConceptState s)
        => new(s.CalibrationSamples, s.MeanStatedConfidence, s.MeanCorrectness, s.BrierScore);

    /// The retention we schedule reps against. 0.90 means: come back when there is a one-in-ten
    /// chance you have lost it — early enough to save it, late enough that the rep is worth
    /// something (M3.3, the spacing effect).
    public const double TargetRetention = 0.90;

    /// **A rebuild destroys everything and recomputes it from the log.** If any belief in here
    /// cannot be reconstructed from history, it should not be in here.
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _db.Evidence.ExecuteDeleteAsync(ct);
        await _db.Misconceptions.ExecuteDeleteAsync(ct);
        await _db.States.ExecuteDeleteAsync(ct);
    }
}
