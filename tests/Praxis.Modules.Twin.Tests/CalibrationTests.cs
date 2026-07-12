using Praxis.Modules.Twin.Learning;

namespace Praxis.Modules.Twin.Tests;

public sealed class CalibrationTests
{
    [Fact]
    public void Being_certain_and_right_is_perfect()
    {
        Assert.Equal(0.0, Calibration.Brier(1.0, correct: true), precision: 9);
    }

    [Fact]
    public void Being_certain_and_wrong_is_the_worst_thing_a_professional_can_do()
    {
        Assert.Equal(1.0, Calibration.Brier(1.0, correct: false), precision: 9);
    }

    [Fact]
    public void Saying_fifty_percent_about_everything_scores_a_quarter()
    {
        // Not calibration. Abstention. The score of a person who has stopped trying to know.
        Assert.Equal(Calibration.UninformativeBrier, Calibration.Brier(0.5, correct: true), precision: 9);
        Assert.Equal(Calibration.UninformativeBrier, Calibration.Brier(0.5, correct: false), precision: 9);
    }

    [Fact]
    public void Knowing_you_do_not_know_scores_as_well_as_knowing_you_do()
    {
        // Saying "10%" and being wrong is nearly as good as saying "90%" and being right. Honest
        // doubt is a skill, and the metric has to reward it or the learner learns to bluff.
        var honestDoubt = Calibration.Brier(0.10, correct: false);
        var honestConfidence = Calibration.Brier(0.90, correct: true);

        Assert.Equal(honestDoubt, honestConfidence, precision: 9);
        Assert.True(honestDoubt < 0.02);
    }

    // ── The two learners with the same score ─────────────────────────────────

    /// The whole reason calibration is a first-class metric. Two learners, both right 80% of the
    /// time. One is trustworthy. The other is dangerous. The score cannot tell them apart.
    [Fact]
    public void Two_learners_with_identical_scores_can_be_entirely_different_people()
    {
        var honest = Run(confidence: _ => 0.80, correct: i => i % 5 != 0);      // right 80%, says 80%
        var bluffer = Run(confidence: _ => 0.99, correct: i => i % 5 != 0);     // right 80%, says 99%

        Assert.Equal(honest.MeanCorrectness, bluffer.MeanCorrectness, precision: 9);

        Assert.True(
            honest.BrierScore < bluffer.BrierScore,
            "The learner who knew what they knew scored no better on calibration than the one who "
            + "bluffed. Calibration is measuring nothing.");

        // And the gap says which is which.
        Assert.True(honest.Overconfidence < 0.05);
        Assert.True(bluffer.Overconfidence > 0.15);
    }

    [Fact]
    public void Overconfidence_is_positive_when_they_think_they_are_better_than_they_are()
    {
        var state = Run(confidence: _ => 0.95, correct: i => i % 2 == 0);   // says 95%, right 50%

        Assert.True(state.Overconfidence > 0.4);
    }

    [Fact]
    public void Underconfidence_is_negative_and_also_worth_telling_them()
    {
        // They hold it and do not know they hold it, and are revising something they already have.
        var state = Run(confidence: _ => 0.30, correct: _ => true);

        Assert.True(state.Overconfidence < -0.6);
    }

    // ── The cheating signature (C-6) ─────────────────────────────────────────

    /// **A learner who outsources their answers gets good scores and catastrophic calibration.**
    ///
    /// They cannot tell you which ones they got right, because they were not the one getting them
    /// right. This is the signature — and PRAXIS never has to block anything to see it.
    [Fact]
    public void Outsourced_work_shows_up_as_high_scores_and_no_self_knowledge()
    {
        // Right 90% of the time. Confidence uncorrelated with outcome — because the confidence is
        // theirs and the answers are not.
        var cheat = CalibrationState.Empty;
        var rng = new Random(42);

        for (var i = 0; i < 40; i++)
        {
            var correct = i % 10 != 0;                     // 90% correct
            var confidence = rng.NextDouble() * 0.4 + 0.3; // guessing at their own performance

            cheat = Calibration.Observe(cheat, confidence, correct);
        }

        Assert.True(cheat.MeanCorrectness >= 0.85, "The simulated cheat is not scoring well.");
        Assert.True(
            Calibration.LooksOutsourced(cheat),
            $"Correctness {cheat.MeanCorrectness:F2} with Brier {cheat.BrierScore:F3} did not trip "
            + "the signature. Someone who genuinely knows 90% of the material can usually tell you "
            + "*which* 90%.");
    }

    [Fact]
    public void An_honest_high_performer_is_never_accused()
    {
        // The false positive that would matter. Someone who genuinely knows the material knows
        // which questions they will get right, so their Brier is low and nothing trips.
        var honest = CalibrationState.Empty;

        for (var i = 0; i < 40; i++)
        {
            var correct = i % 10 != 0;
            var confidence = correct ? 0.92 : 0.20;   // they can feel the hard ones coming

            honest = Calibration.Observe(honest, confidence, correct);
        }

        Assert.True(honest.MeanCorrectness >= 0.85);
        Assert.False(Calibration.LooksOutsourced(honest));
        Assert.Equal(1.0, Calibration.AssuranceMultiplier(honest), tolerance: 0.15);
    }

    [Fact]
    public void An_honest_struggler_is_never_accused_either()
    {
        // Low scores and poor calibration is just someone who is lost. It is not the signature, and
        // treating it as one would punish the people who need help most.
        var struggling = CalibrationState.Empty;

        for (var i = 0; i < 20; i++)
        {
            struggling = Calibration.Observe(struggling, 0.75, correct: i % 3 == 0);
        }

        Assert.True(struggling.MeanCorrectness < 0.5);
        Assert.False(Calibration.LooksOutsourced(struggling));
    }

    [Fact]
    public void The_signature_never_fires_on_too_little_evidence()
    {
        // Accusing someone on n=2 would be exactly the overconfidence this metric exists to catch.
        var barely = Calibration.Observe(CalibrationState.Empty, 0.9, correct: true);
        barely = Calibration.Observe(barely, 0.2, correct: true);

        Assert.False(Calibration.IsMeaningful(barely.Samples));
        Assert.False(Calibration.LooksOutsourced(barely));

        // And a newcomer is not penalised for having just arrived.
        Assert.Equal(1.0, Calibration.AssuranceMultiplier(barely));
    }

    // ── Assurance ────────────────────────────────────────────────────────────

    [Fact]
    public void Perfect_calibration_costs_nothing()
    {
        var perfect = CalibrationState.Empty;

        for (var i = 0; i < 10; i++)
        {
            perfect = Calibration.Observe(perfect, 1.0, correct: true);
        }

        Assert.Equal(1.0, Calibration.AssuranceMultiplier(perfect), precision: 6);
    }

    [Fact]
    public void The_cheating_signature_costs_a_lot_but_does_not_block_anything()
    {
        var cheat = CalibrationState.Empty;

        for (var i = 0; i < 20; i++)
        {
            cheat = Calibration.Observe(cheat, 0.5, correct: true);   // aces everything, knows nothing about itself
        }

        var multiplier = Calibration.AssuranceMultiplier(cheat);

        Assert.True(multiplier < 0.5, "The signature barely discounted the evidence.");

        // But it is still positive. The evidence is discounted, not destroyed, and the learner is
        // never accused of anything — the Ledger simply records how much it is worth.
        Assert.True(multiplier > 0.0);
    }

    // ── Replayability ────────────────────────────────────────────────────────

    [Fact]
    public void Incremental_updates_agree_exactly_with_a_full_recompute()
    {
        // The Twin applies events one at a time, but a rebuild must land on the same numbers. If
        // the running mean drifted from the true mean, replaying a learner's history would produce
        // a different learner.
        var confidences = new[] { 0.9, 0.2, 0.75, 0.5, 0.99, 0.1, 0.6, 0.85 };
        var outcomes = new[] { true, false, true, false, true, false, true, true };

        var running = CalibrationState.Empty;

        for (var i = 0; i < confidences.Length; i++)
        {
            running = Calibration.Observe(running, confidences[i], outcomes[i]);
        }

        var trueMeanConfidence = confidences.Average();
        var trueMeanCorrect = outcomes.Count(o => o) / (double)outcomes.Length;
        var trueBrier = confidences
            .Zip(outcomes, (c, o) => Calibration.Brier(c, o))
            .Average();

        Assert.Equal(trueMeanConfidence, running.MeanStatedConfidence, precision: 12);
        Assert.Equal(trueMeanCorrect, running.MeanCorrectness, precision: 12);
        Assert.Equal(trueBrier, running.BrierScore, precision: 12);
    }

    [Fact]
    public void Calibration_is_a_pure_function_of_the_sequence()
    {
        var a = Run(_ => 0.7, i => i % 3 != 0);
        var b = Run(_ => 0.7, i => i % 3 != 0);

        Assert.Equal(a, b);
    }

    private static CalibrationState Run(
        Func<int, double> confidence,
        Func<int, bool> correct,
        int n = 20)
    {
        var state = CalibrationState.Empty;

        for (var i = 0; i < n; i++)
        {
            state = Calibration.Observe(state, confidence(i), correct(i));
        }

        return state;
    }
}
