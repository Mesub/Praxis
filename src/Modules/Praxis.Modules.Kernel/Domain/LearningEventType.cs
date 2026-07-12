namespace Praxis.Modules.Kernel.Domain;

/// The closed set of things that can happen to a learner. Typed payloads for each land in M1.3.
///
/// Note what is absent, and note that it is absent *on purpose*:
///
///   There is no ConceptCompleted. There is no MarkedAsLearned. There is no CourseFinished.
///
/// A learner cannot declare mastery, and neither can we. Mastery is only ever *inferred* from
/// evidence, and it decays. If someone ever adds a "completed" event here, they have not added
/// a feature — they have deleted the product (CANON §0).
public enum LearningEventType
{
    // ── Session lifecycle ───────────────────────────────────────────────────
    SessionStarted = 1,
    SessionCompleted = 2,

    /// The learner walked away. This is signal, not an error — it is how the Motivation
    /// Engine learns that a session was too long, too hard, or badly timed.
    SessionAbandoned = 3,

    // ── Instruction ─────────────────────────────────────────────────────────
    ConceptTaught = 10,

    /// A rung of the hint ladder was spent. Costs from the Answer Budget (I-5).
    HintTaken = 11,

    /// The budget ran out. **This is not a path to the answer** — it is a compile signal. The
    /// concept gets rescheduled, never revealed (CANON §1.5).
    AnswerBudgetExhausted = 12,

    /// The learner wrote down exactly where their reasoning broke. Misconception evidence (I-6),
    /// never mastery evidence.
    StuckNoteWritten = 13,

    /// **The learner said something.**
    ///
    /// This is what LR-3 gates on: the tutor may not take two turns in a row, and the ladder does
    /// not advance without a learner utterance. A hint delivered to somebody who has not spoken yet
    /// is not scaffolding — it is a lecture with a price tag.
    LearnerResponded = 14,

    // ── Evidence produced ───────────────────────────────────────────────────
    AnswerGiven = 20,
    ExplanationRecorded = 21,
    ArtifactSubmitted = 22,
    PredictionMade = 23,
    PredictionResolved = 24,

    /// A specific wrong mental model was identified in this learner (I-6).
    MisconceptionDetected = 25,

    /// **The learner made something**, and it earns back Answer Budget (CANON §1.3).
    ///
    /// *"Hints cost. Production earns."* Note that a **genuine reasoned wrong attempt** is one of
    /// these: PRAXIS pays you to fail properly, because a system that only ever pays for correctness
    /// has taught you to guess, stall, or quietly leave.
    ProductionRecorded = 26,

    // ── Adjudication ────────────────────────────────────────────────────────
    /// Something the learner produced was accepted as proof. The only path to mastery.
    EvidenceAccepted = 30,

    EvidenceRejected = 31,

    /// The grader declined to decide. Written as pending, never as passed (R-1).
    EvidenceAbstained = 32,

    // ── Memory ──────────────────────────────────────────────────────────────
    RepDue = 40,
    RepAnswered = 41,
}
