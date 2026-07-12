using Praxis.Modules.Twin.Learning;

namespace Praxis.Modules.Twin.Tests;

/// Forgetting — the thing every other learning product pretends does not happen.
public sealed class FsrsTests
{
    [Fact]
    public void Retrievability_is_one_the_moment_you_learn_it()
    {
        Assert.Equal(1.0, Fsrs.Retrievability(stability: 10, daysElapsed: 0), precision: 9);
    }

    /// **What "stability" means, in English.** It is the number of days until you have a 90% chance
    /// of still being able to retrieve it. If this is not exactly 0.9, the word is a lie and every
    /// interval the scheduler computes is arbitrary.
    [Theory]
    [InlineData(1.0)]
    [InlineData(7.0)]
    [InlineData(30.0)]
    [InlineData(365.0)]
    public void At_exactly_S_days_retrievability_is_ninety_percent(double stability)
    {
        Assert.Equal(0.90, Fsrs.Retrievability(stability, daysElapsed: stability), precision: 9);
    }

    /// The factor is derived from the decay exponent so that R(S) = 0.9 exactly. It falls out as
    /// 19/81 — the published FSRS constant — which is a useful check that the curve is the real one
    /// and not something that merely looks like it.
    [Fact]
    public void The_curve_is_the_real_fsrs_curve()
    {
        Assert.Equal(19.0 / 81.0, Fsrs.Factor, precision: 12);
    }

    [Fact]
    public void Knowledge_bleeds_whether_or_not_anyone_is_looking()
    {
        const double stability = 10.0;

        var day1 = Fsrs.Retrievability(stability, 1);
        var day10 = Fsrs.Retrievability(stability, 10);
        var day90 = Fsrs.Retrievability(stability, 90);

        Assert.True(day1 > day10);
        Assert.True(day10 > day90);

        // Ten-day stability, ninety days untouched: nearly half of it is gone.
        Assert.True(day90 < 0.60, $"After 90 days at S=10, retrievability was still {day90:F2}.");
    }

    [Fact]
    public void A_more_stable_memory_survives_the_same_gap_better()
    {
        var fragile = Fsrs.Retrievability(stability: 2, daysElapsed: 30);
        var durable = Fsrs.Retrievability(stability: 200, daysElapsed: 30);

        Assert.True(durable > fragile);
        Assert.True(durable > 0.9, "A memory held for months should barely notice a 30-day gap.");
    }

    /// **A power law, not an exponential.**
    ///
    /// Exponential forgetting says a memory you have held for a year is as fragile *per day* as one
    /// you learned yesterday. That is false, and it produces schedulers that nag people about things
    /// they will never forget.
    [Fact]
    public void Forgetting_slows_down_the_longer_you_have_held_it()
    {
        const double s = 20.0;

        var lostInFirstWeek = Fsrs.Retrievability(s, 0) - Fsrs.Retrievability(s, 7);
        var lostInThirteenthWeek = Fsrs.Retrievability(s, 84) - Fsrs.Retrievability(s, 91);

        // Same seven days, twelve weeks apart, and the later week costs well under half as much.
        Assert.True(
            lostInFirstWeek > lostInThirteenthWeek * 2,
            $"The first week cost {lostInFirstWeek:F4} and the thirteenth cost "
            + $"{lostInThirteenthWeek:F4}. Forgetting is not slowing down — this curve is behaving "
            + "exponentially, and it will nag learners about things they will never forget.");
    }

    [Fact]
    public void A_concept_never_learned_cannot_be_retrieved()
    {
        Assert.Equal(0.0, Fsrs.Retrievability(stability: 0, daysElapsed: 1));
    }

    [Fact]
    public void The_due_date_is_when_retrievability_will_hit_the_target()
    {
        // The rep queue's entire job, and it must agree with the curve it is scheduling against.
        const double s = 14.0;

        var days = Fsrs.DaysUntil(s, targetRetention: 0.90);

        Assert.Equal(s, days, precision: 6);
        Assert.Equal(0.90, Fsrs.Retrievability(s, days), precision: 6);
    }

    [Fact]
    public void Asking_for_higher_retention_brings_the_rep_forward()
    {
        const double s = 30.0;

        var at90 = Fsrs.DaysUntil(s, 0.90);
        var at95 = Fsrs.DaysUntil(s, 0.95);

        Assert.True(at95 < at90, "Wanting to remember more should mean reviewing sooner.");
        Assert.Equal(0.95, Fsrs.Retrievability(s, at95), precision: 6);
    }

    // ── The spacing effect ───────────────────────────────────────────────────

    /// **The single most important line in the file.**
    ///
    /// The harder it was to recall, the more recalling it helped. A rep you aced with the answer
    /// fresh in mind teaches you almost nothing; one you dragged back from the edge of forgetting is
    /// worth weeks.
    ///
    /// Get this backwards and you have built a nagging app: it would review everything constantly,
    /// each review would be trivially easy, and none of them would do anything.
    [Fact]
    public void Retrieving_something_you_nearly_forgot_is_worth_far_more_than_retrieving_something_fresh()
    {
        const double s = 20.0;
        const double d = 5.0;

        var justSawIt = Fsrs.StabilityAfterRecall(s, d, retrievabilityAtReview: 0.99);
        var nearlyLostIt = Fsrs.StabilityAfterRecall(s, d, retrievabilityAtReview: 0.60);

        Assert.True(
            nearlyLostIt > justSawIt,
            $"A rep at the edge of forgetting ({nearlyLostIt:F1}) was worth no more than one on "
            + $"something just seen ({justSawIt:F1}). This is a nagging app, not a scheduler.");

        // And the difference is not marginal — it is the whole reason to space anything.
        Assert.True((nearlyLostIt - s) > (justSawIt - s) * 3);
    }

    [Fact]
    public void A_successful_rep_always_makes_the_memory_more_durable()
    {
        foreach (var s in new[] { 1.0, 5.0, 30.0, 200.0 })
        {
            foreach (var d in new[] { 1.0, 5.0, 9.0 })
            {
                foreach (var r in new[] { 0.5, 0.75, 0.9, 0.99 })
                {
                    var after = Fsrs.StabilityAfterRecall(s, d, r);

                    Assert.True(
                        after >= s,
                        $"S={s}, D={d}, R={r}: a successful recall made the memory *less* durable "
                        + $"({after:F2}).");
                }
            }
        }
    }

    [Fact]
    public void Stable_memories_gain_proportionally_less_from_each_rep()
    {
        // Diminishing returns. Otherwise intervals explode and the learner is told they will never
        // need to see something again — which is exactly the lie PRAXIS exists to refuse.
        var youngGain = Fsrs.StabilityAfterRecall(5, 5, 0.8) / 5;
        var oldGain = Fsrs.StabilityAfterRecall(300, 5, 0.8) / 300;

        Assert.True(oldGain < youngGain);
    }

    [Fact]
    public void A_hard_concept_consolidates_less_per_rep_than_an_easy_one()
    {
        var easy = Fsrs.StabilityAfterRecall(20, difficulty: 2, retrievabilityAtReview: 0.8);
        var hard = Fsrs.StabilityAfterRecall(20, difficulty: 9, retrievabilityAtReview: 0.8);

        Assert.True(hard < easy);
    }

    // ── Lapses ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_lapse_collapses_stability()
    {
        var after = Fsrs.StabilityAfterLapse(stability: 100, difficulty: 5);

        Assert.True(after < 100 * 0.2, $"A lapse left stability at {after:F1} of 100.");
    }

    [Fact]
    public void A_lapse_does_not_reset_to_zero_because_relearning_is_faster_than_learning()
    {
        // Treat a returning learner like a beginner and they will leave.
        var after = Fsrs.StabilityAfterLapse(stability: 100, difficulty: 5);

        Assert.True(after > 0, "A lapse erased the memory entirely.");
        Assert.True(after >= 0.5);
    }

    [Fact]
    public void A_lapse_never_makes_a_memory_more_stable()
    {
        foreach (var s in new[] { 0.6, 1.0, 10.0, 100.0, 1000.0 })
        {
            foreach (var d in new[] { 1.0, 5.0, 10.0 })
            {
                Assert.True(Fsrs.StabilityAfterLapse(s, d) <= s, $"S={s}, D={d}: forgetting helped.");
            }
        }
    }

    // ── Difficulty is the learner's, not the concept's ───────────────────────

    [Fact]
    public void Failure_moves_difficulty_harder_than_success_moves_it_easier()
    {
        // Asymmetric on purpose. One lucky success should not undo the evidence of a struggle.
        const double start = 5.0;

        var afterSuccess = start - Fsrs.UpdateDifficulty(start, correct: true);
        var afterFailure = Fsrs.UpdateDifficulty(start, correct: false) - start;

        Assert.True(afterFailure > afterSuccess);
    }

    [Fact]
    public void Difficulty_stays_on_its_scale_however_long_the_streak()
    {
        var d = 5.0;

        for (var i = 0; i < 100; i++)
        {
            d = Fsrs.UpdateDifficulty(d, correct: true);
        }

        Assert.Equal(1.0, d);

        for (var i = 0; i < 100; i++)
        {
            d = Fsrs.UpdateDifficulty(d, correct: false);
        }

        Assert.Equal(10.0, d);
    }

    [Fact]
    public void A_brutal_concept_earns_less_from_its_first_success_than_a_trivial_one()
    {
        // It is likelier to have been a fluke.
        Assert.True(Fsrs.InitialStability(9) < Fsrs.InitialStability(2));
        Assert.True(Fsrs.InitialStability(10) >= 0.5);
    }

    // ── Belief decay ─────────────────────────────────────────────────────────

    /// **Belief decays towards the prior, not towards zero.**
    ///
    /// Forgetting does not leave you knowing *less than a stranger*. And decaying to zero would have
    /// the Twin assert a certainty it never earned — "this person definitely cannot do this" — about
    /// someone who did it perfectly a year ago.
    [Fact]
    public void Forgetting_returns_you_to_a_strangers_knowledge_not_below_it()
    {
        const double prior = 0.10;

        var afterAges = Fsrs.DecayBelief(pKnows: 0.95, prior, stability: 5, daysElapsed: 100_000);

        // Never below a stranger's knowledge...
        Assert.True(afterAges > prior - 1e-9);

        // ...and, given long enough, indistinguishable from it. (The tail is heavy — that is what a
        // power law means — so "long enough" is genuinely long.)
        Assert.True(afterAges < prior + 0.02, $"After centuries, belief was still {afterAges:F3}.");
    }

    [Fact]
    public void Belief_is_untouched_at_the_moment_evidence_was_produced()
    {
        Assert.Equal(0.88, Fsrs.DecayBelief(0.88, 0.10, stability: 20, daysElapsed: 0), precision: 9);
    }

    [Fact]
    public void A_held_concept_left_alone_for_ninety_days_falls_below_threshold()
    {
        // The promise the whole product makes: you do not complete a concept, you *hold* it, and it
        // bleeds unless refreshed.
        const double held = 0.90;
        const double threshold = 0.80;

        var stability = Fsrs.StabilityAfterRecall(Fsrs.InitialStability(5), 5, 0.9);

        var after90 = Fsrs.DecayBelief(held, 0.10, stability, daysElapsed: 90);

        Assert.True(
            after90 < threshold,
            $"After 90 untouched days the learner still 'holds' this at {after90:F2}. Nothing rots, "
            + "so nothing was ever really learned.");
    }

    [Fact]
    public void Decay_is_a_pure_function_of_elapsed_time()
    {
        // No clock is read inside. A projection that reads the wall clock is not replayable, and the
        // Ledger stops being verifiable.
        var a = Fsrs.DecayBelief(0.9, 0.1, 12.5, 31);
        var b = Fsrs.DecayBelief(0.9, 0.1, 12.5, 31);

        Assert.Equal(a, b);
    }
}
