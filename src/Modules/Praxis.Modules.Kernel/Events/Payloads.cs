using Praxis.Modules.Kernel.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Events;

/// Every payload declares which event it is. The pairing is checked at startup (see
/// <see cref="EventCatalogue"/>), so a payload without an event — or an event without a
/// payload — fails a test rather than a learner.
public interface IEventPayload
{
    static abstract LearningEventType EventType { get; }
}

// ── Session lifecycle ───────────────────────────────────────────────────────

public sealed record SessionStarted(Guid SessionId, int PlannedMinutes)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.SessionStarted;
}

public sealed record SessionCompleted(Guid SessionId, int ActualMinutes, int EvidenceProduced)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.SessionCompleted;
}

/// The learner walked away. Signal, not failure: it is how E13 learns that a session was too
/// long, too hard, or badly timed.
public sealed record SessionAbandoned(Guid SessionId, int MinutesIn, string? AtStep)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.SessionAbandoned;
}

// ── Instruction ─────────────────────────────────────────────────────────────

/// Something was explained to the learner. Note what this does NOT do: it does not raise
/// mastery. Being taught is not evidence of understanding — that is the entire thesis.
public sealed record ConceptTaught(Guid SessionId, string Source, int Seconds)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.ConceptTaught;
}

/// A rung of the hint ladder was spent, and it cost the learner from their Answer Budget (I-5).
public sealed record HintTaken(Guid SessionId, HintRung Rung, int BudgetRemaining)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.HintTaken;
}

public enum HintRung
{
    Orient = 1,
    Probe = 2,
    Analogy = 3,
    Partial = 4,

    /// The last rung is still not "the answer". It is the smallest scaffold that leaves the
    /// learner with something to produce.
    Full = 5,
}

// ── Evidence produced ───────────────────────────────────────────────────────

/// The learner stated a confidence BEFORE seeing the outcome. That is what makes calibration
/// measurable, and it is why cheating shows up as good scores with catastrophic calibration (C-6).
public sealed record AnswerGiven(
    Guid SessionId,
    Guid ItemId,
    EvidenceType EvidenceType,
    bool Correct,
    double StatedConfidence,
    int Seconds)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.AnswerGiven;
}

public sealed record ExplanationRecorded(
    Guid SessionId,
    string Transcript,
    double RubricScore,
    int Seconds)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.ExplanationRecorded;
}

public sealed record ArtifactSubmitted(
    Guid SessionId,
    Guid ArtifactId,
    string Kind,
    bool HiddenTestsPassed,
    int TestsPassed,
    int TestsTotal)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.ArtifactSubmitted;
}

/// A falsifiable claim about the world, made before the world settled it (I-3).
public sealed record PredictionMade(
    Guid LoopId,
    string Claim,
    double StatedConfidence,
    DateTime ResolvesAt)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.PredictionMade;
}

/// Reality answered. The Brier score is the learner's calibration, not their score — being
/// right for the wrong reason is graded separately, and on the same day (R-4).
public sealed record PredictionResolved(
    Guid LoopId,
    bool Correct,
    double BrierScore,
    string GroundTruth)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.PredictionResolved;
}

public sealed record MisconceptionDetected(
    Guid MisconceptionId,
    string Name,
    string Evidence)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.MisconceptionDetected;
}

// ── Adjudication ────────────────────────────────────────────────────────────

/// The only path to mastery in the entire system.
///
/// `Assurance` is how much we trust this proof, and it is recorded honestly (C-6). An EXPLAIN
/// graded by a model is worth less than a BUILD that actually ran, and the Twin's Evidence
/// Ceiling (C-4) enforces that difference mathematically rather than by policy.
public sealed record EvidenceAccepted(
    Guid EvidenceId,
    EvidenceType Type,
    double Score,
    double Assurance)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.EvidenceAccepted;
}

public sealed record EvidenceRejected(
    Guid EvidenceId,
    EvidenceType Type,
    string Reason)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.EvidenceRejected;
}

/// The grader declined to decide. Abstention is a first-class output (R-1): "I am not sure this
/// counts as proof" is an honest answer, and it is written as pending — never as passed.
public sealed record EvidenceAbstained(
    Guid EvidenceId,
    EvidenceType Type,
    string Reason,
    bool EscalatedToHuman)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.EvidenceAbstained;
}

// EvidenceType now lives in Praxis.Shared — the Graph names it in its PoM contracts, the Twin
// weighs it, and the Ledger records it. It is domain language, not the kernel's private business.

// ── Memory ──────────────────────────────────────────────────────────────────

public sealed record RepDue(DateTime DueAt, double RetrievabilityAtDue)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.RepDue;
}

public sealed record RepAnswered(
    Guid ItemId,
    bool Correct,
    double StatedConfidence,
    int Seconds,
    double StabilityBefore,
    double StabilityAfter)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.RepAnswered;
}
