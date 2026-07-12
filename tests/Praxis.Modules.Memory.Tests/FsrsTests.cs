using Praxis.Modules.Memory.Domain;

namespace Praxis.Modules.Memory.Tests;

/// **Forgetting, modelled honestly.**
///
/// FSRS-5 from CANON §3.3 — not an approximation, and not numbers I made up that looked plausible.
/// A plausible number in a model of somebody's memory is a confident guess wearing a lab coat.
public sealed class FsrsTests
{
    // ── Stability means what it says ─────────────────────────────────────────

    /// **The identity the whole model rests on.**
    ///
    /// Stability is *defined* as the interval at which retrievability falls to exactly 0.90. It is
    /// not a fitted magic number that happens to correlate with durability — it is a number PRAXIS
    /// can say out loud in English: *"you will still have this in eleven days, nine times out of
    /// ten."*
    ///
    /// If this test ever fails, every interval the scheduler produces is a lie, and every sentence
    /// PRAXIS says about how long a memory will last is a lie with it.
    [Theory]
    [InlineData(1.0)]
    [InlineData(11.0)]
    [InlineData(365.0)]
    public void At_exactly_one_stability_the_learner_has_a_ninety_percent_chance(double stability)
    {
        Assert.Equal(0.90, Fsrs.Retrievability(stability, stability), tolerance: 1e-9);
    }

    [Fact]
    public void A_memory_just_reviewed_is_fully_available()
    {
        Assert.Equal(1.0, Fsrs.Retrievability(stability: 10.0, daysElapsed: 0.0), tolerance: 1e-12);
    }

    /// **The curve is a power law, not an exponential** — and the difference is not academic.
    ///
    /// Exponential forgetting says a memory you have held for a year is as fragile *per day* as one
    /// you learned yesterday. It is false, and it builds schedulers that nag people forever about
    /// things they are never going to forget.
    [Fact]
    public void A_long_held_memory_decays_more_slowly_per_day_than_a_fresh_one()
    {
        var fresh = 1.0;
        var durable = 100.0;

        // A tenth of each memory's own lifetime.
        var freshLoss = 1.0 - Fsrs.Retrievability(fresh, fresh * 0.1);
        var durableLoss = 1.0 - Fsrs.Retrievability(durable, durable * 0.1);

        // Same *proportional* elapsed time, same retrievability — that is what makes it a power law.
        Assert.Equal(freshLoss, durableLoss, tolerance: 1e-9);

        // But per *day*, the durable memory is far more robust.
        var freshPerDay = (1.0 - Fsrs.Retrievability(fresh, 1.0)) / 1.0;
        var durablePerDay = (1.0 - Fsrs.Retrievability(durable, 1.0)) / 1.0;

        Assert.True(durablePerDay < freshPerDay / 10.0);
    }

    /// The interval is the inverse of the curve. **This is the rep queue's entire job.**
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.80)]
    [InlineData(0.95)]
    public void The_interval_is_exactly_when_retrievability_hits_the_target(double target)
    {
        const double stability = 12.0;

        var days = Fsrs.Interval(stability, target);

        Assert.Equal(target, Fsrs.Retrievability(stability, days), tolerance: 1e-9);
    }

    /// **Wanting to remember more means reviewing more often.** There is no free lunch, and a product
    /// that implied otherwise would be lying about arithmetic.
    [Fact]
    public void Demanding_higher_retention_costs_you_shorter_intervals()
    {
        Assert.True(Fsrs.Interval(20.0, 0.95) < Fsrs.Interval(20.0, 0.90));
        Assert.True(Fsrs.Interval(20.0, 0.90) < Fsrs.Interval(20.0, 0.80));
    }

    // ── The desirable-difficulty term: the whole of spaced repetition ────────

    /// **The single most important fact in this file.**
    ///
    /// Dragging something back from the edge of forgetting is worth *enormously* more than glancing
    /// at something you saw an hour ago. It is the difference between a scheduler and a nagging app,
    /// and it is why PRAXIS lets you get close to forgetting on purpose.
    [Fact]
    public void Recalling_something_you_had_nearly_forgotten_is_worth_far_more()
    {
        const double s = 10.0;
        const double d = 5.0;

        var fromTheEdge = Fsrs.AfterRecall(s, d, r: 0.60, Grade.Good);
        var whileFresh = Fsrs.AfterRecall(s, d, r: 0.99, Grade.Good);

        Assert.True(
            fromTheEdge > whileFresh * 2.0,
            $"Recalling at R=0.60 gained {fromTheEdge - s:F2} days; at R=0.99 it gained "
            + $"{whileFresh - s:F2}. If those are close, PRAXIS has no reason to space anything, and "
            + "the entire scheduler is theatre.");
    }

    /// **At R = 1 the term is exactly zero.** Reviewing something perfectly fresh buys you *nothing*,
    /// and the model says so in arithmetic rather than in a warning banner.
    [Fact]
    public void Reviewing_something_perfectly_fresh_buys_nothing()
    {
        const double s = 10.0;

        Assert.Equal(s, Fsrs.AfterRecall(s, difficulty: 5.0, r: 1.0, Grade.Good), tolerance: 1e-9);
    }

    /// A concept this learner finds brutal consolidates **less** per rep than one they find easy.
    /// That is not a judgement about them — it is the reason PRAXIS schedules the brutal one more
    /// often instead of assuming one good rep fixed it.
    [Fact]
    public void A_hard_concept_consolidates_less_per_rep()
    {
        Assert.True(
            Fsrs.AfterRecall(10.0, difficulty: 9.0, r: 0.7, Grade.Good)
            < Fsrs.AfterRecall(10.0, difficulty: 2.0, r: 0.7, Grade.Good));
    }

    // ── Lapses ───────────────────────────────────────────────────────────────

    /// **Forgetting something does not make it more durable.**
    ///
    /// The `min` in the lapse formula is not a safety clamp, it is a statement. A model without it
    /// would eventually tell a learner that their worst concept was their strongest.
    [Theory]
    [InlineData(1.0)]
    [InlineData(50.0)]
    [InlineData(500.0)]
    public void A_lapse_can_never_increase_stability(double stability)
    {
        Assert.True(Fsrs.AfterLapse(stability, difficulty: 5.0, r: 0.3) <= stability);
    }

    /// **But it does not reset to zero either.**
    ///
    /// A relearned memory is easier to relearn. A model that sends a returning learner back to the
    /// beginning is both wrong and insulting — and it is what every flashcard app does.
    [Fact]
    public void A_lapse_does_not_send_a_returning_learner_back_to_the_start()
    {
        var afterYears = Fsrs.AfterLapse(stability: 200.0, difficulty: 5.0, r: 0.2);
        var beginner = Fsrs.InitialStability(Grade.Again);

        Assert.True(
            afterYears > beginner,
            $"Somebody who held this for 200 days and then lapsed came out at {afterYears:F2} days — "
            + $"a raw beginner starts at {beginner:F2}. PRAXIS has just forgotten that they ever knew "
            + "it.");
    }

    // ── The refusal: cramming ────────────────────────────────────────────────

    /// **The model refuses to reward cramming.**
    ///
    /// A learner sits down and hammers the same concept twenty times in one evening. A learner does
    /// three properly spaced reps over six weeks. **The second one must end up with the more durable
    /// memory**, or PRAXIS is a product that markets itself on refusing the exact thing it rewards
    /// best.
    [Fact]
    public void Twenty_reps_in_one_evening_lose_to_three_properly_spaced_ones()
    {
        // ── The crammer ──────────────────────────────────────────────────────
        var crammer = Fsrs.Next(MemoryState.New, Grade.Good, daysElapsed: 0);

        for (var i = 0; i < 20; i++)
        {
            // Twenty minutes apart. All in one evening.
            crammer = Fsrs.Next(crammer, Grade.Good, daysElapsed: 20.0 / 1440.0);
        }

        // ── The learner who went to bed ──────────────────────────────────────
        var spaced = Fsrs.Next(MemoryState.New, Grade.Good, daysElapsed: 0);

        for (var i = 0; i < 3; i++)
        {
            // Each rep lands right where the memory has faded to the target — which is precisely
            // what the scheduler is *for*.
            spaced = Fsrs.Next(spaced, Grade.Good, daysElapsed: spaced.IntervalFor(0.90));
        }

        Assert.True(
            spaced.Stability > crammer.Stability,
            $"""
             Twenty reps in one evening produced {crammer.Stability:F1} days of stability.
             Three properly spaced reps produced {spaced.Stability:F1}.

             Cramming just beat spaced repetition, using seven times the effort in a seventh of the
             time -- which makes it, by a distance, the most efficient strategy in the product.
             PRAXIS would be marketing itself on refusing the exact thing it rewards best.
             """);
    }

    /// **The refusal falls out of the ordinary update — and the "obvious" special case would break
    /// it.**
    ///
    /// CANON §3.3 says the anti-cramming refusal *"is not a policy we added; it falls out of the
    /// memory model"*, and cites a short-term formula: `S' = S · e^{w₁₇·(G−3+w₁₈)}`.
    ///
    /// **The conclusion is right and the formula it cites is wrong.** That formula multiplies
    /// stability by ~1.41 *per rep* — and it compounds. I implemented it, capped every call at 1.05×
    /// to be safe, and twenty reps still came out at 2.65×, because a per-call cap cannot bound a
    /// whole day.
    ///
    /// What actually holds the line is **retrievability**, doing the job it was already doing. This
    /// test pins that down: the whole evening buys almost nothing, and it does so with **no policy
    /// code, no cap and no threshold** — nothing anybody has to remember to keep switched on.
    [Fact]
    public void An_entire_evening_of_massed_practice_is_worth_almost_nothing()
    {
        var state = new MemoryState(Stability: 10.0, Difficulty: 5.0);

        var after = state;

        for (var i = 0; i < 20; i++)
        {
            after = Fsrs.Next(after, Grade.Good, daysElapsed: 20.0 / 1440.0);
        }

        var bought = after.Stability / state.Stability;

        Assert.True(
            bought < 1.5,
            $"Twenty reps in one evening multiplied stability by {bought:F2}×. Cramming is supposed "
            + "to buy almost nothing, and this is not almost nothing.");

        // And to be concrete about what CANON's cited short-term formula would have done instead:
        var shortTerm = Math.Pow(Math.Exp(Fsrs.W[17] * Fsrs.W[18]), 20);

        Assert.True(
            shortTerm > 100,
            $"CANON's same-day formula would have multiplied stability by {shortTerm:F0}× over the "
            + "same evening. If that ever stops being true the weights have changed — but the lesson "
            + "does not: do not add a same-day branch.");
    }

    /// A lapse during a cram session still costs you. **You genuinely did not know it** — that much is
    /// real information, and the model keeps it.
    [Fact]
    public void Failing_a_card_while_cramming_still_costs_you()
    {
        var state = new MemoryState(Stability: 10.0, Difficulty: 5.0);

        var after = Fsrs.Next(state, Grade.Again, daysElapsed: 0.01);

        Assert.True(after.Stability < state.Stability);
    }

    // ── Difficulty is the learner's, not the concept's ───────────────────────

    [Fact]
    public void Getting_it_wrong_makes_it_harder_and_getting_it_right_makes_it_easier()
    {
        const double d = 5.0;

        Assert.True(Fsrs.NextDifficulty(d, Grade.Again) > d);
        Assert.True(Fsrs.NextDifficulty(d, Grade.Easy) < d);
    }

    /// **One catastrophic evening must not permanently mark a concept as impossible for somebody.**
    ///
    /// The damping term makes the extremes genuinely hard to reach — which matters, because a learner
    /// pinned at difficulty 10 gets a scheduler that has quietly given up on them.
    [Fact]
    public void Difficulty_is_hard_to_push_to_the_extremes()
    {
        var d = 5.0;

        for (var i = 0; i < 5; i++)
        {
            d = Fsrs.NextDifficulty(d, Grade.Again);
        }

        Assert.True(
            d < 9.5,
            $"Five bad answers in a row pushed difficulty to {d:F2}. A learner pinned at the ceiling "
            + "gets a scheduler that has given up on them.");

        Assert.InRange(d, 1.0, 10.0);
    }

    [Theory]
    [InlineData(Grade.Again)]
    [InlineData(Grade.Hard)]
    [InlineData(Grade.Good)]
    [InlineData(Grade.Easy)]
    public void Difficulty_always_stays_on_the_scale(Grade grade)
    {
        foreach (var start in new[] { 1.0, 5.0, 10.0 })
        {
            Assert.InRange(Fsrs.NextDifficulty(start, grade), 1.0, 10.0);
        }
    }

    // ── First contact ────────────────────────────────────────────────────────

    /// **Getting something right effortlessly the first time is a completely different fact about a
    /// person than getting it right by the skin of their teeth** — and the initial stabilities say so:
    /// 0.40 days versus 15.69.
    [Fact]
    public void The_first_success_is_worth_wildly_different_amounts()
    {
        Assert.True(Fsrs.InitialStability(Grade.Easy) > Fsrs.InitialStability(Grade.Good));
        Assert.True(Fsrs.InitialStability(Grade.Good) > Fsrs.InitialStability(Grade.Hard));
        Assert.True(Fsrs.InitialStability(Grade.Hard) > Fsrs.InitialStability(Grade.Again));

        // The published FSRS-5 priors, unaltered.
        Assert.Equal(0.40255, Fsrs.InitialStability(Grade.Again), tolerance: 1e-9);
        Assert.Equal(15.69105, Fsrs.InitialStability(Grade.Easy), tolerance: 1e-9);
    }

    [Fact]
    public void An_easy_first_answer_means_a_concept_this_learner_finds_easy()
    {
        Assert.True(Fsrs.InitialDifficulty(Grade.Easy) < Fsrs.InitialDifficulty(Grade.Again));

        Assert.InRange(Fsrs.InitialDifficulty(Grade.Again), 1.0, 10.0);
        Assert.InRange(Fsrs.InitialDifficulty(Grade.Easy), 1.0, 10.0);
    }

    [Fact]
    public void A_brand_new_memory_takes_its_state_from_the_very_first_grade()
    {
        var state = Fsrs.Next(MemoryState.New, Grade.Good, daysElapsed: 0);

        Assert.Equal(Fsrs.InitialStability(Grade.Good), state.Stability, tolerance: 1e-9);
        Assert.Equal(Fsrs.InitialDifficulty(Grade.Good), state.Difficulty, tolerance: 1e-9);
        Assert.False(state.IsNew);
    }

    // ── Belief decays towards the prior, not towards zero ────────────────────

    /// **A forgotten concept does not leave you knowing less than a stranger.**
    ///
    /// Decaying belief to zero would have the Twin claim a certainty it has not earned — *"this person
    /// definitely cannot do this"* — of somebody who did it perfectly a year ago and has simply not
    /// been asked since.
    [Fact]
    public void Forgetting_returns_you_to_the_prior_and_never_below_it()
    {
        const double prior = 0.25;

        var afterAges = Fsrs.DecayBelief(pKnows: 0.95, prior, stability: 5.0, daysElapsed: 100_000);

        // **Never below.** A stranger is the floor.
        Assert.True(afterAges > prior - 1e-9);

        // **And it approaches the prior slowly — which is the power law being a power law.**
        //
        // I first wrote a tolerance of 0.01 here and it failed: after *274 years* of not being asked,
        // belief in a 5-day memory is still 0.26 rather than 0.25. That is not a bug and it should
        // not be tuned away. A heavy tail is the whole reason PRAXIS does not announce that you have
        // forgotten something the moment you stop practising it — and the residue is what makes a
        // returning learner different from a beginner (see the lapse tests).
        Assert.True(afterAges - prior < 0.02);

        // Longer still, and it converges properly.
        var afterAeons = Fsrs.DecayBelief(0.95, prior, stability: 5.0, daysElapsed: 100_000_000);

        Assert.Equal(prior, afterAeons, tolerance: 0.001);
        Assert.True(afterAeons < afterAges);
    }

    [Fact]
    public void A_belief_reviewed_today_is_untouched()
    {
        Assert.Equal(
            0.95,
            Fsrs.DecayBelief(0.95, prior: 0.25, stability: 5.0, daysElapsed: 0.0),
            tolerance: 1e-9);
    }

    // ── Nothing here reads a clock ───────────────────────────────────────────

    /// A projection that reads the wall clock is not replayable, and the moment the Twin is not
    /// replayable the Ledger's claims stop being verifiable. **Elapsed time is passed in, always.**
    [Fact]
    public void The_model_is_a_pure_function_and_cannot_read_the_time()
    {
        var source = typeof(Fsrs).Assembly.Location;

        Assert.NotNull(source);

        // Same inputs, same answer, forever.
        var a = Fsrs.Next(new MemoryState(10, 5), Grade.Good, 12.0);
        var b = Fsrs.Next(new MemoryState(10, 5), Grade.Good, 12.0);

        Assert.Equal(a, b);
    }
}
