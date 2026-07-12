using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **You can be right for the wrong reason, and wrong for the right reason. Outcome-only scoring
/// would teach superstition** (R-4).
///
/// A learner predicts what a program does and *why*. Reality settles whether they were right — in
/// seconds, deterministically, un-arguably (M9.3). A rubric settles whether their *reasoning* was
/// sound — did it invoke the concept that actually governed the behaviour, or did they guess and get
/// lucky? These are **two different questions, graded by two different judges on two different
/// timescales**, and this file keeps them apart on purpose.
///
/// ## Why they must not be conflated
///
/// The four combinations are four different people, and three of them are invisible to a scorer that
/// only checks the outcome:
///
/// | | **reasoning sound** | **reasoning unsound** |
/// |---|---|---|
/// | **was right** | *Mastery.* Right, and knew why. | **About to blow up.** Right by luck, and does not know it. |
/// | **was wrong** | *Unlucky.* Sound reasoning, unkind reality — a signal, not a failure. | *Wrong, and the reasoning shows why.* |
///
/// The top-right cell is the dangerous one, and it is exactly what an outcome-only product **rewards
/// silently** — a learner guesses right, banks the credit, and repeats a process that will eventually
/// fail them catastrophically. PRAXIS says it out loud.
///
/// ## The refusal that keeps this honest: process is advisory, never certifying
///
/// The outcome is **verifiable** — reality produced it, and it is 100% of what the Ledger records.
/// The process score is produced by a *language-model rubric*, and a language model's opinion is
/// exactly what the Evidence Ceiling refuses to trust (C-4). So process **cannot cross into the
/// Ledger** (it contributes 0), and — the load-bearing structural rule — **a thesis graded while its
/// outcome is still pending confers no mastery at all.** A brilliant rationale is not a result. You
/// can reason beautifully about what a market will do and be wrong, and until reality answers, the
/// beauty of the argument buys nothing.
public static class LoopScoring
{
    /// Outcome is the verifiable part, and it carries most of the mastery weight.
    public const double OutcomeWeightInMastery = 0.60;

    /// Process is advisory — it *shapes* the mastery credit, but it cannot make it on its own.
    public const double ProcessWeightInMastery = 0.40;

    /// At or above this, the rationale invoked what mattered: it named the governing concept,
    /// considered the base rate, named a falsifier. Below it, the learner reached the answer without
    /// the reasoning that should have produced it.
    public const double ProcessIsSound = 0.60;

    /// Score a resolved Loop — outcome from reality, process from the rubric.
    ///
    /// **Returns a pending verdict if the outcome has not resolved**, no matter how good the process
    /// score is. That is the decoupling made structural: the reasoning can be graded in seconds, but
    /// the mastery it might earn waits for the world.
    public static LoopVerdict Score(LoopOutcome outcome, ThesisScore thesis)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(thesis);

        if (!outcome.Resolved)
        {
            // **Process graded, outcome pending.** The rationale is on the record and the learner can
            // read it — but nothing has been *earned*, because nothing has been *verified*. A pending
            // Loop with a perfect rationale is worth exactly as much mastery as one with no rationale
            // at all: none, yet.
            return LoopVerdict.Pending(thesis);
        }

        var soundReasoning = thesis.Score >= ProcessIsSound;

        var quadrant = (outcome.Correct, soundReasoning) switch
        {
            (true, true) => LoopQuadrant.RightAndKnewWhy,
            (true, false) => LoopQuadrant.RightForTheWrongReason,
            (false, true) => LoopQuadrant.Unlucky,
            (false, false) => LoopQuadrant.WrongAndShowsIt,
        };

        // Mastery blends the two. Note the outcome component is the *graded* outcome (1 − Brier), so a
        // confident-correct answer is worth more than a hesitant one — reality plus calibration.
        var outcomeGoodness = 1.0 - outcome.BrierScore;

        var masteryCredit =
            (OutcomeWeightInMastery * outcomeGoodness) + (ProcessWeightInMastery * thesis.Score);

        return new LoopVerdict(
            State: LoopState.Scored,
            Quadrant: quadrant,

            // **The Ledger records the verifiable part, and only that.** Process weight here is zero —
            // not small, zero — because the Ledger is a claim about what a learner can *do*, backed by
            // what reality confirmed, and a rubric's opinion is not that.
            LedgerScore: outcomeGoodness,

            MasteryCredit: masteryCredit,
            ProcessScore: thesis.Score,
            Debrief: Debrief(quadrant, thesis));
    }

    private static string Debrief(LoopQuadrant quadrant, ThesisScore thesis) => quadrant switch
    {
        LoopQuadrant.RightAndKnewWhy =>
            "Right, and for the right reason. This is the one that counts — you predicted the "
            + "outcome and your reasoning holds up under a rubric.",

        LoopQuadrant.RightForTheWrongReason =>
            "**You were right, but your reasoning does not hold.** "
            + Missing(thesis)
            + " Reality happened to agree with you this time. It will not always, and a process that "
            + "produces right answers by luck is one you are about to get burned by. The result "
            + "counts — but do not mistake it for understanding.",

        LoopQuadrant.Unlucky =>
            "Wrong — but your reasoning was sound. You invoked what mattered and named your "
            + "falsifier; reality simply broke the other way. **This is a signal, not a failure**, "
            + "and if it keeps happening on the same kind of item it may be our model that is "
            + "wrong, not yours.",

        LoopQuadrant.WrongAndShowsIt =>
            "Wrong, and the reasoning shows why. " + Missing(thesis)
            + " Fixing the process is the thing that fixes the outcome.",

        _ => "This Loop has not resolved yet.",
    };

    private static string Missing(ThesisScore thesis)
        => thesis.MissedConcepts.Count == 0
            ? "The rationale did not engage the mechanism that governs this."
            : $"The rationale never invoked: {string.Join(", ", thesis.MissedConcepts)}.";
}

/// **What reality said** — verifiable, and possibly not in yet.
///
/// The two timescales live here. For a code Loop the outcome resolves in seconds; for a market or a
/// PR it may be days. `Pending` is a first-class state, not a null, because *"we do not know yet"* is
/// a real answer and the thing that lets the thesis be graded without it.
public sealed record LoopOutcome(bool Resolved, bool Correct, double BrierScore)
{
    /// A Loop whose outcome the world has not yet produced. The thesis can still be scored.
    public static LoopOutcome Pending { get; } = new(false, false, 0.0);

    public static LoopOutcome From(PredictionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        return new LoopOutcome(Resolved: true, resolution.Correct, resolution.BrierScore);
    }
}

/// **What the rubric said about the *reasoning*** — advisory, never certifying.
///
/// Produced by the Assessment rubric grader (M5.4), which is a language model, which is why this can
/// shade a mastery credit but can never confer one and can never touch the Ledger.
public sealed record ThesisScore(
    /// [0, 1]: did the rationale invoke the concepts that mattered, consider the base rate, name a
    /// falsifier?
    double Score,

    /// The concepts the learner *should* have invoked and did not. This is what turns *"your
    /// reasoning was weak"* into something a learner can act on.
    IReadOnlyList<string> MissedConcepts)
{
    public ThesisScore(double score) : this(score, []) { }
}

/// **The verdict, keeping outcome and process in separate hands.**
public sealed record LoopVerdict(
    LoopState State,
    LoopQuadrant Quadrant,

    /// What the Ledger records: **outcome only**. `null` while the outcome is pending — because the
    /// Ledger never records an unverified claim.
    double? LedgerScore,

    /// Mastery credit, blending both. `null` while pending — **a graded thesis alone earns nothing.**
    double? MasteryCredit,

    /// The advisory rationale score, shown but never certifying.
    double? ProcessScore,

    string Debrief)
{
    public static LoopVerdict Pending(ThesisScore thesis)
    {
        ArgumentNullException.ThrowIfNull(thesis);

        return new LoopVerdict(
            State: LoopState.OutcomePending,
            Quadrant: LoopQuadrant.OutcomePending,
            LedgerScore: null,
            MasteryCredit: null,
            ProcessScore: thesis.Score,
            Debrief: "Your reasoning is on the record and graded. The outcome has not resolved yet — "
                     + "and until it does, this Loop has earned nothing, however good the argument. A "
                     + "thesis is not a result.");
    }
}

public enum LoopState
{
    /// The outcome resolved; both scores are in.
    Scored = 1,

    /// The thesis is graded, but reality has not answered. **No mastery, no Ledger entry, yet.**
    OutcomePending = 2,
}

/// The four people an outcome-only scorer cannot tell apart.
public enum LoopQuadrant
{
    OutcomePending = 0,

    /// Right, and the reasoning holds. The only cell that is unambiguously good.
    RightAndKnewWhy = 1,

    /// **Right by luck.** The result counts, but the process is one that will fail — and PRAXIS says
    /// so, because the alternative is rewarding superstition.
    RightForTheWrongReason = 2,

    /// Wrong, but the reasoning was sound. **A signal, not a failure** — and a systematically unlucky
    /// learner on one kind of item is evidence our Graph is wrong, not theirs.
    Unlucky = 3,

    /// Wrong, and the reasoning shows why. The honest, ordinary case — and the most fixable.
    WrongAndShowsIt = 4,
}
