using Praxis.Modules.Compiler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Tests;

/// **The refusal that separates a product from a slot machine.**
///
/// Every other company in this industry would take the 20-minutes-a-day learner's money and sell
/// them the AI Engineer track. PRAXIS runs the simulation, finds that they will forget faster than
/// they learn, and says so — out loud, before they pay.
public sealed class DecayWallTests
{
    /// **The test CANON asks for by name (C-1) — and it passes for a different reason than CANON
    /// predicted.**
    ///
    /// CANON says the wall is a *divergence*: below some pace, forgetting outruns learning and the
    /// objective becomes structurally unreachable. **I simulated it, and that is not what happens.**
    ///
    /// Spaced repetition forbids divergence. Every successful retrieval pushes the next one
    /// exponentially further away, so the maintenance bill grows sub-linearly and always converges.
    /// A model in which it did not would be a model of despair rather than of memory — real
    /// spaced-repetition users hold tens of thousands of items on twenty minutes a day.
    ///
    /// What actually happens is worse in a quieter way: the learner arrives **three and a half years
    /// later**, having spent most of every session treading water. The wall is a **horizon**, not a
    /// divergence — and PRAXIS refuses the plan for the reason that is true rather than the reason
    /// that was assumed.
    [Fact]
    public void A_large_objective_at_twenty_minutes_a_day_is_unreachable_in_any_horizon_a_human_would_accept()
    {
        var plan = PlanOf(concepts: 180, minutesEach: 60, minutesPerDay: 20);

        var verdict = DecayWall.Simulate(plan, minutesPerDay: 20);

        Assert.False(
            verdict.Reachable,
            $"180 concepts at 20 minutes a day was reported as REACHABLE within "
            + $"{verdict.HorizonDays} days. PRAXIS is about to sell someone a plan it should not "
            + "believe in, and they will spend eighteen months proving it wrong at their own expense.");

        // They did not fail to move. They moved, slowly, and ran out of life.
        Assert.True(verdict.ConceptsReached > 0);

        Assert.True(
            verdict.ProgressMade < 0.95,
            "They were essentially there. The horizon is the wrong knob and the refusal is unfair.");
    }

    /// **Honesty about the mechanism.** It *does* converge, eventually. The reason to refuse the plan
    /// is that "eventually" is not a promise anyone can act on.
    [Fact]
    public void It_converges_eventually_which_is_exactly_why_a_horizon_is_needed()
    {
        var plan = PlanOf(concepts: 180, minutesEach: 60, minutesPerDay: 20);

        var withInfinitePatience = DecayWall.Simulate(plan, 20, horizonDays: 365 * 6);

        Assert.True(
            withInfinitePatience.Reachable,
            "Even given six years the learner never arrives. That would be genuine divergence — and "
            + "it would mean the model has no spacing effect in it, i.e. that nobody can ever learn "
            + "anything.");

        Assert.True(
            withInfinitePatience.DaysToObjective > DecayWall.DefaultHorizonDays,
            "It arrived inside the default horizon after all, so there was nothing to refuse.");
    }

    [Fact]
    public void The_same_objective_is_reachable_at_a_serious_daily_commitment()
    {
        var plan = PlanOf(concepts: 180, minutesEach: 60, minutesPerDay: 90);

        var verdict = DecayWall.Simulate(plan, minutesPerDay: 90);

        Assert.True(verdict.Reachable, "180 concepts is unreachable even at 90 minutes a day. The "
            + "simulation is not modelling learning; it is modelling despair.");

        Assert.Equal(180, verdict.ConceptsReached);
    }

    /// More minutes must never make things worse. A non-monotonic simulation produces verdicts that
    /// are noise, and the number it recommends would be a coin flip.
    [Fact]
    public void More_minutes_a_day_never_leaves_the_learner_further_behind()
    {
        var plan = PlanOf(concepts: 120, minutesEach: 60, minutesPerDay: 30);

        var previous = 0;

        foreach (var minutesPerDay in new[] { 15, 20, 30, 45, 60, 90, 120 })
        {
            var reached = DecayWall.Simulate(plan, minutesPerDay).ConceptsReached;

            Assert.True(
                reached >= previous,
                $"At {minutesPerDay} min/day the learner reached {reached} concepts, having reached "
                + $"{previous} on *less* time. The simulation is not monotonic, so none of its "
                + "verdicts can be trusted.");

            previous = reached;
        }
    }

    [Fact]
    public void A_small_objective_is_reachable_even_on_a_thin_budget()
    {
        // The wall is not "you need an hour a day". It depends on the size of what you are trying
        // to hold. A learner with a modest objective and twenty minutes is fine, and a product that
        // told them otherwise would be lying in the other direction.
        var plan = PlanOf(concepts: 8, minutesEach: 60, minutesPerDay: 20);

        var verdict = DecayWall.Simulate(plan, minutesPerDay: 20);

        Assert.True(verdict.Reachable);
    }

    // ── The mechanism ────────────────────────────────────────────────────────

    /// **Why the wall exists at all.**
    ///
    /// Every concept a learner holds must be periodically retrieved or it goes away. As the number
    /// they hold grows, the daily maintenance bill grows with it. The wall is the point where that
    /// bill consumes the whole day — after which every minute goes on keeping what they have, and
    /// nothing is left to move forward.
    [Fact]
    public void The_maintenance_bill_grows_with_what_the_learner_holds()
    {
        var small = DecayWall.Simulate(PlanOf(20, 60, 60), 60);
        var large = DecayWall.Simulate(PlanOf(150, 60, 60), 60);

        Assert.True(
            large.PeakMaintenanceMinutesPerDay > small.PeakMaintenanceMinutesPerDay,
            "Holding 150 concepts cost no more to maintain than holding 20. The simulation has no "
            + "maintenance in it, and the wall it reports is a fiction.");
    }

    /// **The spacing effect is the only reason learning is possible at all.**
    ///
    /// Each successful retrieval pushes the next one further away, so a concept's long-run
    /// maintenance cost falls. Without it, the bill would grow without bound and *every* objective
    /// would be unreachable — which is plainly not what happens to real people.
    [Fact]
    public void Without_the_spacing_effect_nothing_would_ever_be_learnable()
    {
        // Proof that the model is not simply pessimistic: a real learner, at a real pace, arrives.
        var verdict = DecayWall.Simulate(PlanOf(180, 60, 120), 120);

        Assert.True(verdict.Reachable);
        Assert.True(
            verdict.DaysToObjective < DecayWall.DefaultHorizonDays,
            "Even at two hours a day the learner never arrives. Reps are not consolidating, and "
            + "the model describes a person who cannot learn.");
    }

    // ── What PRAXIS says instead ─────────────────────────────────────────────

    /// **The number, not the platitude.**
    ///
    /// "Try harder" is useless. "You need 45 minutes a day" is actionable, and it is a claim PRAXIS
    /// is willing to be held to.
    [Fact]
    public void It_computes_the_minutes_a_day_that_would_actually_work()
    {
        var plan = PlanOf(concepts: 180, minutesEach: 60, minutesPerDay: 20);

        var needed = DecayWall.MinutesPerDayToMakeReachable(plan);

        Assert.NotNull(needed);
        Assert.True(needed > 20, "The number it recommends is the number that already failed.");

        // And the recommendation actually works.
        Assert.True(DecayWall.Simulate(plan, needed.Value).Reachable);

        // While one minute less does not — so it is the *smallest* honest answer, not a padded one.
        Assert.False(DecayWall.Simulate(plan, needed.Value - 1).Reachable);
    }

    /// **What they CAN become in the time they have.**
    ///
    /// PRAXIS does not shrug and sell the plan anyway. It finds the furthest point along the path
    /// the learner will actually reach, and offers that instead — a narrower objective it genuinely
    /// believes in.
    [Fact]
    public void It_offers_the_furthest_point_the_learner_will_actually_reach()
    {
        var plan = PlanOf(concepts: 180, minutesEach: 60, minutesPerDay: 20);

        var reachable = DecayWall.ReachablePrefix(plan, minutesPerDay: 20);

        Assert.NotEmpty(reachable);
        Assert.True(reachable.Count < 180, "The 'narrower' objective is the whole thing again.");

        // It is a genuine prefix of the plan — a real place to stop, not an arbitrary subset.
        Assert.Equal(plan.Steps.Take(reachable.Count).Select(s => s.Slug), reachable.Select(s => s.Slug));

        // And PRAXIS believes in what it is offering: at their real pace, they arrive.
        var narrower = new Plan(plan.Objective, reachable, reachable.Sum(s => s.Minutes), 0, 0, 0);

        Assert.True(
            DecayWall.Simulate(narrower, 20).Reachable,
            "PRAXIS offered a narrower objective that it also does not believe in.");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void An_empty_plan_is_trivially_reachable()
    {
        var verdict = DecayWall.Simulate(
            new Plan(Guid.CreateVersion7(), [], 0, 30, 0, 0), 45);

        Assert.True(verdict.Reachable);
    }

    [Fact]
    public void Zero_minutes_a_day_reaches_nothing()
    {
        var verdict = DecayWall.Simulate(PlanOf(10, 60, 30), 0);

        Assert.False(verdict.Reachable);
    }

    [Fact]
    public void An_objective_too_large_for_any_human_day_is_admitted_to_be_impossible()
    {
        // At some size, the answer is not "find more minutes". It is "this objective is wrong", and
        // PRAXIS must be willing to say so rather than recommending an eight-hour day.
        var absurd = PlanOf(concepts: 5000, minutesEach: 120, minutesPerDay: 60);

        var needed = DecayWall.MinutesPerDayToMakeReachable(absurd);

        Assert.Null(needed);
    }

    [Fact]
    public void The_simulation_is_deterministic()
    {
        var plan = PlanOf(120, 60, 30);

        var a = DecayWall.Simulate(plan, 30);
        var b = DecayWall.Simulate(plan, 30);

        Assert.Equal(a, b);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// A plan of N concepts in a straight line — each one costing the same, each depending on the
    /// last. The simplest shape that can exhibit the wall.
    private static Plan PlanOf(int concepts, double minutesEach, int minutesPerDay)
    {
        var steps = Enumerable.Range(1, concepts)
            .Select(i => new PlanStep(
                ConceptId: Guid.CreateVersion7(),
                Slug: $"concept-{i:D3}",
                Position: i,
                Minutes: minutesEach,
                CumulativeMinutes: minutesEach * i,
                DayReached: (int)Math.Ceiling(minutesEach * i / minutesPerDay),
                Kind: StepKind.Learn))
            .ToList();

        return new Plan(
            Objective: steps[^1].ConceptId,
            Steps: steps,
            TotalMinutes: minutesEach * concepts,
            ConceptsAlreadyHeld: 0,
            ConceptsNeedingRefresh: 0,
            DaysAtStatedPace: minutesEach * concepts / minutesPerDay);
    }
}
