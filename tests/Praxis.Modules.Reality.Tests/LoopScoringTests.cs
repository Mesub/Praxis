using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **You can be right for the wrong reason** — and PRAXIS scores the reason separately from the
/// result, on a separate timescale, and refuses to let a language model's opinion of your reasoning
/// count as a verified outcome.
public sealed class LoopScoringTests
{
    private static LoopOutcome Right(double brier = 0.04) => new(Resolved: true, Correct: true, brier);
    private static LoopOutcome Wrong(double brier = 0.81) => new(Resolved: true, Correct: false, brier);

    private static ThesisScore Sound(params string[] _) => new(0.85);
    private static ThesisScore Weak(params string[] missed) => new(0.30, missed);

    // ── The four quadrants ───────────────────────────────────────────────────

    /// **Right, and knew why.** The only unambiguously good cell.
    [Fact]
    public void Right_with_sound_reasoning_is_mastery()
    {
        var verdict = LoopScoring.Score(Right(), Sound());

        Assert.Equal(LoopQuadrant.RightAndKnewWhy, verdict.Quadrant);
        Assert.Contains("for the right reason", verdict.Debrief, StringComparison.Ordinal);
    }

    /// **The dangerous cell an outcome-only product rewards silently.** Right by luck, and PRAXIS says
    /// so out loud rather than banking it as understanding.
    [Fact]
    public void Right_with_weak_reasoning_is_named_as_luck_not_understanding()
    {
        var verdict = LoopScoring.Score(Right(), Weak("time_monotonicity"));

        Assert.Equal(LoopQuadrant.RightForTheWrongReason, verdict.Quadrant);

        Assert.Contains("about to get burned", verdict.Debrief, StringComparison.Ordinal);
        Assert.Contains("time_monotonicity", verdict.Debrief, StringComparison.Ordinal);

        // The result still counts — reality did say they were right — but it is not called mastery.
        Assert.NotEqual(LoopQuadrant.RightAndKnewWhy, verdict.Quadrant);
    }

    /// **Wrong, but sound — a signal, not a failure.** And the debrief raises the possibility that our
    /// model is the wrong one, not the learner.
    [Fact]
    public void Wrong_with_sound_reasoning_is_unlucky_not_a_failure()
    {
        var verdict = LoopScoring.Score(Wrong(), Sound());

        Assert.Equal(LoopQuadrant.Unlucky, verdict.Quadrant);

        Assert.Contains("signal, not a failure", verdict.Debrief, StringComparison.Ordinal);
        Assert.Contains("our model", verdict.Debrief, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_with_weak_reasoning_is_the_ordinary_fixable_case()
    {
        var verdict = LoopScoring.Score(Wrong(), Weak("base_rate"));

        Assert.Equal(LoopQuadrant.WrongAndShowsIt, verdict.Quadrant);
        Assert.Contains("base_rate", verdict.Debrief, StringComparison.Ordinal);
    }

    // ── The load-bearing refusal: process is advisory ────────────────────────

    /// **The Ledger records the verifiable part, and only that.** Two learners with the *same* outcome
    /// and wildly different reasoning get the *same* Ledger score — because a language model's opinion
    /// of a rationale is exactly what the Evidence Ceiling refuses to trust (C-4).
    [Fact]
    public void Two_learners_with_the_same_outcome_get_the_same_ledger_score()
    {
        var brilliant = LoopScoring.Score(Right(brier: 0.02), new ThesisScore(0.99));
        var lucky = LoopScoring.Score(Right(brier: 0.02), new ThesisScore(0.05));

        Assert.Equal(brilliant.LedgerScore, lucky.LedgerScore);

        // And it is the outcome, not some blend.
        Assert.Equal(1.0 - 0.02, brilliant.LedgerScore!.Value, tolerance: 1e-9);
    }

    /// **A better rationale does move mastery credit** — process is advisory, not *ignored*. It is
    /// 40% of the mastery blend and 0% of the Ledger, and this pins both halves of that.
    [Fact]
    public void A_better_rationale_lifts_mastery_credit_but_not_the_ledger()
    {
        var strong = LoopScoring.Score(Right(brier: 0.04), new ThesisScore(0.90));
        var weak = LoopScoring.Score(Right(brier: 0.04), new ThesisScore(0.30));

        Assert.True(strong.MasteryCredit > weak.MasteryCredit, "Process should shade mastery credit.");
        Assert.Equal(strong.LedgerScore, weak.LedgerScore);

        // The gap is exactly the process weight times the score difference.
        var expectedGap = LoopScoring.ProcessWeightInMastery * (0.90 - 0.30);
        Assert.Equal(expectedGap, strong.MasteryCredit!.Value - weak.MasteryCredit!.Value, tolerance: 1e-9);
    }

    // ── The two timescales ───────────────────────────────────────────────────

    /// **A thesis is not a result.** The reasoning can be graded in seconds; the mastery it might earn
    /// waits for the world. A pending outcome earns **nothing** — no mastery, no Ledger — no matter
    /// how perfect the rationale.
    [Fact]
    public void A_perfect_thesis_with_a_pending_outcome_earns_nothing_yet()
    {
        var verdict = LoopScoring.Score(LoopOutcome.Pending, new ThesisScore(1.0));

        Assert.Equal(LoopState.OutcomePending, verdict.State);
        Assert.Null(verdict.MasteryCredit);
        Assert.Null(verdict.LedgerScore);

        // The reasoning is on the record and readable — it just has not been cashed.
        Assert.Equal(1.0, verdict.ProcessScore);
        Assert.Contains("not a result", verdict.Debrief, StringComparison.Ordinal);
    }

    /// The same thesis, once reality answers, *does* earn — proving the pending state was a genuine
    /// "not yet", not a permanent discard.
    [Fact]
    public void Once_the_outcome_resolves_the_thesis_pays_out()
    {
        var pending = LoopScoring.Score(LoopOutcome.Pending, new ThesisScore(0.9));
        var resolved = LoopScoring.Score(Right(), new ThesisScore(0.9));

        Assert.Null(pending.MasteryCredit);
        Assert.NotNull(resolved.MasteryCredit);
        Assert.True(resolved.MasteryCredit > 0);
    }

    // ── Built from a real prediction resolution ──────────────────────────────

    /// The outcome side plugs straight into the PREDICT resolutions from M9.3.
    [Fact]
    public void The_outcome_comes_from_a_prediction_resolution()
    {
        var resolution = new PredictionResolution(Correct: true, "raises ZeroDivisionError", "raises ZeroDivisionError", 0.9);

        var outcome = LoopOutcome.From(resolution);

        Assert.True(outcome.Resolved);
        Assert.True(outcome.Correct);
        Assert.Equal(resolution.BrierScore, outcome.BrierScore, tolerance: 1e-9);
    }

    /// A missed-concepts list makes the weak-reasoning debriefs actionable rather than vague.
    [Fact]
    public void A_weak_rationale_with_no_named_gaps_still_reads_cleanly()
    {
        var verdict = LoopScoring.Score(Right(), new ThesisScore(0.2));

        Assert.Equal(LoopQuadrant.RightForTheWrongReason, verdict.Quadrant);
        Assert.Contains("governs this", verdict.Debrief, StringComparison.Ordinal);
    }
}
