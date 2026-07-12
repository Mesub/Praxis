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

/// **The ladder, and the rung that is not on it.**
///
/// Five rungs, each a stronger scaffold than the last. Look at what is missing from the bottom:
/// there is no `FullReveal`, no `Answer`, no `Solution`, and there is no *number* below
/// `PartialScaffold` for one to occupy. CANON §1.2 lists a sixth rung, `L5 FULL_REVEAL`, and marks
/// it *"Gated. In LEARN mode this rung does not exist."*
///
/// It does not exist here either — **not as a gated member, not as a member behind a feature flag,
/// and not as a member that throws.** A gated member is a member somebody can ungate under
/// deadline; an absent one is an absent one. Going broke on this ladder does not get you the
/// answer. It gets you **rescheduled** (§1.5).
///
/// The scaffold gets stronger as you descend, and the *cost* rises with it (see `HintCost`) — so
/// the ladder is a slope, not a button.
public enum HintRung
{
    /// **L0 — free, and unlimited.** Restate the goal; name what is known and unknown; point at the
    /// relevant surface. **Zero new content**: it may not introduce a single fact the learner's Twin
    /// does not already hold.
    ///
    /// Free because orienting yourself is a *skill*, and a skill you are charged for is a skill you
    /// learn not to use.
    Orient = 0,

    /// **L1 — one Socratic question that narrows the search space.** It may quote the learner's own
    /// words back at them. It may never contain the answer, or a fact they could simply read off it.
    Probe = 1,

    /// **L2 — map it onto something they already hold.** The Twin must say `P(knows) ≥ 0.7` for the
    /// thing being mapped *from*: an analogy the learner does not hold is not a hint, it is a second
    /// unknown.
    Analogy = 2,

    /// **L3 — work a different problem, start to finish, thinking aloud.**
    ///
    /// *Isomorphic but different.* Never **this** problem: the example must have different surface
    /// features and **a different answer**. The moment it shares an answer, it is not an example —
    /// it is the answer with extra steps.
    WorkedExample = 3,

    /// **L4 — the structure, and only the structure.** The first step; the skeleton; the equation
    /// with a hole in it; the function signature.
    ///
    /// **It does not fill the hole.** This is the last rung, and it is *still not the answer* — it is
    /// the smallest scaffold that leaves the learner with something to produce. If it ever fills the
    /// hole, PRAXIS has become an answer key with a personality.
    PartialScaffold = 4,
}

/// **The budget is gone, and the answer is still not coming.**
///
/// CANON §1.5, in as many words: *"Not this: 'OK fine, here's the answer.' That path does not exist
/// in the code."*
///
/// Zero budget is a **compile signal, not a surrender**. The item stops, the learner writes a stuck
/// note saying exactly where their reasoning broke, and that note is graded as **misconception
/// evidence, not mastery evidence** (I-6). The concept is then rescheduled — behind its missing
/// prerequisites if that is what broke, or with a different carrier if the explanation was what
/// broke.
public sealed record AnswerBudgetExhausted(Guid SessionId, HintRung DeepestRungReached, int HintsTaken)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.AnswerBudgetExhausted;
}

/// **The learner said something**, and the ladder is now allowed to move (LR-3).
///
/// Their words are **data**, not instructions. They are recorded as an utterance, and they are
/// rendered to the tutor as a quoted utterance — never spliced into its instructions. A learner who
/// types "ignore all previous instructions" has typed a sentence.
public sealed record LearnerResponded(Guid SessionId, string Said)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.LearnerResponded;
}

/// *"Where, exactly, does your reasoning break?"*
///
/// Mandatory when the budget runs out, and **the most valuable two sentences in the session** — it
/// is the only artefact in PRAXIS that tells us *why* someone is stuck rather than merely *that*
/// they are. It is never mastery evidence. It is a diagnosis.
public sealed record StuckNoteWritten(Guid SessionId, string Note)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.StuckNoteWritten;
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

/// **The learner made something.** It earns back Answer Budget (CANON §1.3).
///
/// Note what this payload does **not** carry: the *amount*. The writer says what happened; the
/// projection decides what it was worth (the same rule as `HintTaken.BudgetRemaining` — M6.1). A
/// tutor that could name its own price would eventually name a generous one.
public sealed record ProductionRecorded(Guid SessionId, ProductionKind Kind)
    : IEventPayload
{
    public static LearningEventType EventType => LearningEventType.ProductionRecorded;
}

/// What a learner did that PRAXIS is willing to pay for.
///
/// Read the list and notice what is **not** on it: logged in, watched the video, completed the
/// module, kept a streak. Every one of these is something they **made**.
public enum ProductionKind
{
    /// Reached it on the free rungs alone. The generation effect: you did the work.
    CorrectUnaided = 1,

    /// An explanation that passed a rubric. Putting it in your own words is not a *report on*
    /// learning — it *is* learning.
    SelfExplanation = 2,

    /// A Feynman teach-back that passed. The strongest production signal there is.
    TeachBack = 3,

    /// Correct on a *sibling* item, unaided. Transfer, not memorisation.
    Transfer = 4,

    /// **They caught their own error before the tutor named it.** Metacognition — the single most
    /// valuable behaviour in the product, because a learner who can hear their own reasoning creak
    /// will not need PRAXIS for very much longer (I-10).
    CaughtOwnError = 5,

    /// **A real, reasoned, incorrect answer. PRAXIS pays you to fail properly.**
    ///
    /// Not to fail — to fail *having tried*, which is rarer and much more useful. Productive failure
    /// is where most learning actually happens, and a system that only ever pays for correctness has
    /// taught its learners to guess, to stall, or to quietly leave.
    GenuineWrongAttempt = 6,

    /// A diagram that passed a structural rubric. Dual coding: a drawing forces you to commit to a
    /// structure that prose lets you smudge.
    Drawing = 7,
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
