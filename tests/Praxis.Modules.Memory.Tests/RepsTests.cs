using Praxis.Modules.Memory.Domain;

namespace Praxis.Modules.Memory.Tests;

/// **What is due now, what is overdue, and what is about to rot.**
///
/// Every SRS in the world sorts this queue by how overdue each card is. These tests show that the
/// ordering is **inverted at the top** — it hands the learner their single worst-value item and calls
/// it the most urgent thing they own.
public sealed class RepsTests
{
    private static MemoryState Held(double stability = 20.0, double difficulty = 5.0)
        => new(stability, difficulty);

    /// Days elapsed such that retrievability is exactly `r`. **Derived from the curve**, so if the
    /// constants ever move, these tests move with them rather than quietly starting to prove
    /// something else.
    private static double At(double r, double stability = 20.0)
        => Fsrs.Interval(stability, r);

    // ── The finding ──────────────────────────────────────────────────────────

    /// **The sweet spot is a property of the curve, not a number I chose.**
    ///
    /// This test does not trust `PeakRetrievability`. It re-derives it by sweeping the model, and it
    /// checks the peak lands in the same place at wildly different stabilities — because a "constant"
    /// that only holds for one memory is not a constant, it is a coincidence.
    ///
    /// If the FSRS weights are ever refitted and the peak moves, **this fails** rather than the
    /// constant silently becoming folklore.
    [Theory]
    [InlineData(5.0)]
    [InlineData(20.0)]
    [InlineData(120.0)]
    public void The_sweet_spot_is_where_the_model_says_it_is(double stability)
    {
        var state = Held(stability);

        var best = (R: 0.0, Value: double.MinValue);

        for (var r = 0.99; r > 0.05; r -= 0.005)
        {
            var value = Reps.ValuePerMinute(state, At(r, stability));

            if (value > best.Value)
            {
                best = (r, value);
            }
        }

        Assert.Equal(Reps.PeakRetrievability, best.R, tolerance: 0.02);
    }

    /// **The one that overturns the standard queue.**
    ///
    /// A concept you have almost entirely forgotten is worth **less** than one you have barely
    /// forgotten — because you will probably fail it, and a failed rep is not a rep. It is a lapse:
    /// the memory collapses and you spend fifteen minutes relearning something you thought you knew.
    ///
    /// So "most overdue" is not "most urgent". It is close to **least worth doing**.
    [Fact]
    public void Something_you_have_entirely_forgotten_is_worth_less_than_something_barely_faded()
    {
        var state = Held();

        var almostGone = Reps.ValuePerMinute(state, At(0.10));
        var barelyFaded = Reps.ValuePerMinute(state, At(0.95));
        var inTheWindow = Reps.ValuePerMinute(state, At(Reps.PeakRetrievability));

        Assert.True(
            almostGone < barelyFaded,
            $"A concept at R=0.10 scored {almostGone:F2} per minute; one at R=0.95 scored "
            + $"{barelyFaded:F2}. If the forgotten one wins, then sorting by 'most overdue' is "
            + "defensible after all — and this whole module is wrong.");

        // And both are dwarfed by the item actually in the window.
        Assert.True(inTheWindow > barelyFaded * 2);
        Assert.True(inTheWindow > almostGone * 2);
    }

    /// **The queue is sorted by what it is worth, not by what you owe.**
    [Fact]
    public void The_most_overdue_concept_is_not_the_top_of_the_queue()
    {
        var abandoned = new DueRep(Guid.CreateVersion7(), "abandoned", Held(), At(0.55));
        var ripe = new DueRep(Guid.CreateVersion7(), "ripe", Held(), At(Reps.PeakRetrievability));
        var nearlyFresh = new DueRep(Guid.CreateVersion7(), "nearly-fresh", Held(), At(0.88));

        var plan = Reps.Build([abandoned, ripe, nearlyFresh]);

        Assert.Equal("ripe", plan.Due[0].Slug);

        // The most overdue thing in the list is *not* first — and a naive queue would have put it
        // there.
        Assert.NotEqual("abandoned", plan.Due[0].Slug);

        // And the ordering is strictly by value.
        Assert.True(plan.Due[0].ValuePerMinute >= plan.Due[^1].ValuePerMinute);
    }

    // ── The refusal: PRAXIS will not show you a queue of things you have lost ─

    /// **Below 50% you are more likely to fail it than pass it. So it is not a review.**
    ///
    /// Putting it in a three-minute review queue is not an optimisation error. It is **a false
    /// statement about how the learner's evening is going to go** — and it is the lie that turns a
    /// study tool into a wall of four hundred red cards that people quietly stop opening.
    [Fact]
    public void A_concept_you_would_probably_fail_is_not_in_the_review_queue()
    {
        var lost = new DueRep(Guid.CreateVersion7(), "long-gone", Held(), At(0.20));
        var ripe = new DueRep(Guid.CreateVersion7(), "ripe", Held(), At(0.70));

        var plan = Reps.Build([lost, ripe]);

        Assert.DoesNotContain(plan.Due, d => d.Slug == "long-gone");

        // **Not hidden, though.** Named, counted, and handed back to the plan at what a re-learn
        // actually costs.
        Assert.Contains(plan.Lost, l => l.Slug == "long-gone");
        Assert.NotNull(plan.AboutWhatYouLost);

        Assert.Contains(
            "three-minute price tag", plan.AboutWhatYouLost!, StringComparison.Ordinal);
    }

    /// The line is drawn **where the coin flips**, not where it looked tidy.
    [Fact]
    public void The_line_is_drawn_where_you_become_more_likely_to_fail_than_to_pass()
    {
        Assert.Equal(0.50, Reps.NotAReviewBelow, tolerance: 1e-9);

        Assert.Equal(
            RepUrgency.Lost,
            Reps.Classify(Held(), At(Reps.NotAReviewBelow - 0.05)));

        Assert.NotEqual(
            RepUrgency.Lost,
            Reps.Classify(Held(), At(Reps.NotAReviewBelow + 0.05)));
    }

    /// **A queue that quotes you three minutes a card and then hands you fifteen is not a schedule,
    /// it is a trap.**
    [Fact]
    public void The_queue_quotes_what_a_rep_will_actually_cost()
    {
        // A rep you will pass 95% of the time costs about a rep.
        Assert.Equal(
            Reps.RepMinutes,
            Reps.ExpectedMinutes(0.95),
            tolerance: Reps.RepMinutes * 0.25);

        // A coin-flip costs nearly twice that, because half the time it is a re-learn.
        Assert.True(Reps.ExpectedMinutes(0.50) > Reps.RepMinutes * 2);

        // And the plan's total is the expected bill, not the nominal one.
        var plan = Reps.Build([
            new DueRep(Guid.CreateVersion7(), "a", Held(), At(0.85)),
            new DueRep(Guid.CreateVersion7(), "b", Held(), At(0.60)),
        ]);

        Assert.Equal(plan.Due.Sum(d => d.ExpectedMinutes), plan.ExpectedMinutes, tolerance: 1e-9);

        Assert.True(
            plan.ExpectedMinutes > plan.Due.Count * Reps.RepMinutes,
            "The queue is quoting the best case. It should be quoting the expected case.");
    }

    // ── Being early is not a virtue ──────────────────────────────────────────

    /// **Repping something you still remember perfectly buys almost nothing** (M8.1), so PRAXIS does
    /// not put it in front of you and let you feel productive about it.
    [Fact]
    public void A_concept_still_fresh_is_not_in_the_queue_at_all()
    {
        var fresh = new DueRep(Guid.CreateVersion7(), "fresh", Held(), At(0.97));

        var plan = Reps.Build([fresh]);

        Assert.Empty(plan.Due);
        Assert.Equal(RepUrgency.Fresh, Reps.Classify(Held(), At(0.97)));
    }

    /// **Nothing worth doing is a real answer.**
    ///
    /// Not *"0 cards due — keep your streak alive!"* PRAXIS optimises for the learner leaving (I-10),
    /// and an empty queue is the product working, not the product failing.
    [Fact]
    public void An_empty_queue_is_said_plainly_and_not_filled_with_something()
    {
        var plan = Reps.Build([
            new DueRep(Guid.CreateVersion7(), "fresh", Held(), At(0.98)),
        ]);

        Assert.True(plan.NothingWorthDoing);
        Assert.Empty(plan.Due);
        Assert.Null(plan.AboutWhatYouLost);
    }

    /// A concept never proved is **not due** — it is not learned. That is the plan's business, and
    /// putting it in the review queue would be the queue claiming credit for work nobody has done.
    [Fact]
    public void Something_never_learned_is_not_something_you_are_behind_on()
    {
        var never = new DueRep(Guid.CreateVersion7(), "unlearned", MemoryState.New, DaysSinceReview: 400);

        var plan = Reps.Build([never]);

        Assert.Empty(plan.Due);
        Assert.Empty(plan.Lost);

        Assert.Equal(RepUrgency.NeverLearned, Reps.Classify(MemoryState.New, 400));
    }

    // ── Rotting ──────────────────────────────────────────────────────────────

    /// **Past the peak and falling.** Every day this waits, it is worth less *and* likelier to fail —
    /// which is the one combination that genuinely justifies the word "urgent".
    [Fact]
    public void Past_the_peak_a_concept_is_rotting_and_it_gets_worse_every_day()
    {
        var state = Held();

        Assert.Equal(RepUrgency.Rotting, Reps.Classify(state, At(0.55)));
        Assert.Equal(RepUrgency.Ripe, Reps.Classify(state, At(0.80)));

        // Worth less tomorrow than today.
        var today = Reps.ValuePerMinute(state, At(0.55));
        var inAWeek = Reps.ValuePerMinute(state, At(0.55) + 7);

        Assert.True(inAWeek < today);
    }

    [Fact]
    public void The_rotting_ones_are_callable_out_of_the_plan()
    {
        var plan = Reps.Build([
            new DueRep(Guid.CreateVersion7(), "rotting", Held(), At(0.55)),
            new DueRep(Guid.CreateVersion7(), "ripe", Held(), At(0.85)),
        ]);

        Assert.Single(plan.Rotting);
        Assert.Equal("rotting", plan.Rotting[0].Slug);
    }

    /// Same learner, same day, same queue. **A queue that reshuffles every time you look at it is not
    /// scheduling, it is stalling.**
    [Fact]
    public void The_queue_is_deterministic()
    {
        var reps = new[]
        {
            new DueRep(Guid.CreateVersion7(), "a", Held(), At(0.70)),
            new DueRep(Guid.CreateVersion7(), "b", Held(), At(0.70)),
            new DueRep(Guid.CreateVersion7(), "c", Held(), At(0.85)),
        };

        var first = Reps.Build(reps);
        var second = Reps.Build(reps);

        Assert.Equal(
            first.Due.Select(d => d.Slug),
            second.Due.Select(d => d.Slug));
    }
}
