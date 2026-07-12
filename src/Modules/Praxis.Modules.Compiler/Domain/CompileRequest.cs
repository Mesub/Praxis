using Praxis.Shared;

namespace Praxis.Modules.Compiler.Domain;

/// Everything the Compiler needs, as plain values.
///
/// **It does not know the Graph or the Twin exist.** It is handed a snapshot and returns a plan.
/// That is what makes it a solver rather than a service — and what lets it be tested exhaustively
/// without a database, which is the only way a thing this important gets to be trusted.
public sealed record CompileRequest(
    /// What the learner wants to become able to do. The build target.
    Guid Objective,

    IReadOnlyList<CompilerConcept> Concepts,

    /// How much time they actually have. The Decay Wall (M4.3) is computed against this.
    int MinutesPerDay,

    /// Null means "no deadline" — take as long as it takes.
    DateTime? Deadline = null);

/// One concept, as the Compiler sees it: what the graph says, and what the Twin says.
public sealed record CompilerConcept(
    Guid Id,
    string Slug,

    /// Direct prerequisites. **All of them are required** — this is an AND, not an OR. There is no
    /// choosing the cheaper parent.
    IReadOnlyList<Guid> Requires,

    int EstimatedMinutes,
    MasteryContract Contract,

    // ── What the Twin believes, right now ────────────────────────────────────

    double MasteryCredit,
    bool IsHeld,

    /// How available it is *today*, on the forgetting curve. This is what makes the plan a function
    /// of the learner rather than of the syllabus.
    double Retrievability,

    /// How hard *this person* finds it (FSRS 1–10), not the graph's opinion.
    double LearnerDifficulty);

/// The compiled plan: what to do, in what order, and what it will cost.
///
/// **Ephemeral, always regenerable.** It is not a commitment; it is the current best answer, and it
/// is recompiled the moment anything about the learner changes (M4.4).
public sealed record Plan(
    Guid Objective,
    IReadOnlyList<PlanStep> Steps,

    /// The honest total. Not the syllabus's total — this learner's.
    double TotalMinutes,

    /// What the learner already holds, and therefore never has to do. The cache hits.
    int ConceptsAlreadyHeld,

    /// Concepts they *had* held and have since let rot. They will meet these again — cheaper than
    /// the first time, but not free, and PRAXIS says so rather than pretending they are done.
    int ConceptsNeedingRefresh,

    /// Calendar days at the learner's stated pace. This is what the Decay Wall (M4.3) interrogates.
    double DaysAtStatedPace)
{
    public bool IsEmpty => Steps.Count == 0;
}

/// One thing to do, and why it is where it is.
public sealed record PlanStep(
    Guid ConceptId,
    string Slug,

    /// Where in the order. 1-based, because it is shown to a human.
    int Position,

    /// What this concept costs *this* learner, priced by M4.1.
    double Minutes,

    /// Minutes from the start of the plan to the end of this step. What the progress bar reads.
    double CumulativeMinutes,

    /// Roughly which day they reach it, at their stated pace.
    int DayReached,

    StepKind Kind);

public enum StepKind
{
    /// They have never held it. Everything is ahead of them.
    Learn = 1,

    /// They held it once and have let it rot. Cheaper than learning — relearning is faster than
    /// learning — but **not free**, and the plan refuses to pretend it is done.
    Refresh = 2,

    /// Partly there. The remaining work is the expensive part (M4.1): the never-seen problem and
    /// the build that runs.
    Finish = 3,
}
