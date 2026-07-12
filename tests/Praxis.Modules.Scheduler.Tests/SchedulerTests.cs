using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Scheduler.Domain;

namespace Praxis.Modules.Scheduler.Tests;

/// **One card. One action.**
///
/// The single hardest moment in learning is *starting*, and a screen that presents a choice hands
/// that moment straight back to the person who is demonstrably bad at it. So PRAXIS decides.
///
/// If the card ever becomes a list, PRAXIS has lost.
public sealed class SchedulerTests
{
    // ── The card is one thing ────────────────────────────────────────────────

    [Fact]
    public void The_card_is_one_action_never_a_list()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(
            SessionMinutes: 15,
            Due: [Due("embeddings", 0.85), Due("bm25", 0.88), Due("chunking", 0.80)],
            NextStep: Step("hnsw", 90)));

        // One concept. One thing to do. Everything else is deferred, not displayed.
        Assert.NotEqual(Guid.Empty, card.ConceptId);
        Assert.NotEmpty(card.Slug);
        Assert.True(card.Minutes > 0);

        // And the learner is owed a reason. A scheduler that cannot give one is guessing.
        Assert.NotEmpty(card.Because);
    }

    [Fact]
    public void The_scheduler_never_asks_the_learner_to_choose()
    {
        // Three things due, one plan step, fifteen minutes. A lesser product shows four cards and a
        // filter. PRAXIS picks.
        var card = TodayScheduler.Choose(new SchedulerInputs(
            15,
            [Due("a", 0.9), Due("b", 0.85), Due("c", 0.8)],
            Step("d", 60)));

        Assert.Equal(3, card.AlsoDue.Count);

        // The deferred list exists to make the decision *auditable*, not to be shown by default.
        Assert.All(card.AlsoDue, d => Assert.NotEmpty(d.Reason));
    }

    // ── 1. Rescue beats everything ───────────────────────────────────────────

    /// **A dissolving foundation is not "due" — it is load-bearing.**
    ///
    /// Everything above it in the graph now costs multiples more, and the learner attempting that
    /// work will not experience it as harder. They will experience it as *impossible*, and they will
    /// blame themselves. Nine minutes rescuing it buys back hours they were about to waste.
    [Fact]
    public void A_dissolving_foundation_beats_everything_else()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(
            SessionMinutes: 25,
            Due: [Due("embeddings", 0.35), Due("bm25", 0.88)],
            NextStep: Step("retrieval", 90)));

        Assert.Equal(CardKind.Rescue, card.Kind);
        Assert.Equal("embeddings", card.Slug);

        // And the reason names the real cost — not "you have a rep due".
        Assert.Contains("getting harder while it slips", card.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void The_most_nearly_lost_thing_is_rescued_first()
    {
        // The one whose rescue is about to get four times more expensive.
        var card = TodayScheduler.Choose(new SchedulerInputs(
            25,
            [Due("nearly-gone", 0.20), Due("slipping", 0.60)],
            Step("next", 60)));

        Assert.Equal(CardKind.Rescue, card.Kind);
        Assert.Equal("nearly-gone", card.Slug);
    }

    [Fact]
    public void New_work_waits_until_the_ground_under_it_is_solid()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(
            25, [Due("foundation", 0.30)], Step("the-thing-they-want", 60)));

        Assert.Equal(CardKind.Rescue, card.Kind);

        var deferred = card.AlsoDue.Single(d => d.Slug == "the-thing-they-want");
        Assert.Contains("ground under it is solid", deferred.Reason, StringComparison.Ordinal);
    }

    // ── 2. But reps must never eat the session ───────────────────────────────

    /// **The counterintuitive one, and the trap most schedulers fall into.**
    ///
    /// Clearing the rep queue first *feels* responsible. It is a trap: on a busy day the queue IS the
    /// session, and the learner spends weeks revising and never advances. They are working hard, the
    /// graph is not moving, and they eventually conclude — correctly — that this is going nowhere.
    [Fact]
    public void A_rep_too_large_for_the_session_gives_way_to_moving_forward()
    {
        // A 12-minute rep in a 15-minute session would leave 3 minutes for the plan. That is not a
        // session; that is a chore with a rounding error.
        var card = TodayScheduler.Choose(new SchedulerInputs(
            SessionMinutes: 15,
            Due: [Due("expensive-rep", 0.85, minutes: 12)],
            NextStep: Step("forward", 30)));

        Assert.Equal(CardKind.Learn, card.Kind);
        Assert.Equal("forward", card.Slug);

        Assert.Contains(
            card.AlsoDue,
            d => d.Reason.Contains("the plan cannot wait forever", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cheap_rep_is_taken_because_reps_are_cheap()
    {
        // Three minutes out of fifteen. It keeps the whole edifice standing, and there is still a
        // session left afterwards.
        var card = TodayScheduler.Choose(new SchedulerInputs(
            15, [Due("cheap-rep", 0.85, minutes: 3)], Step("forward", 30)));

        Assert.Equal(CardKind.Rep, card.Kind);
        Assert.Equal("cheap-rep", card.Slug);
        Assert.Equal(3, card.Minutes);
    }

    /// **A deferral must say why it was deferred, and a rep is not a rescue.**
    ///
    /// Both paths defer the plan step, and the first version reused one sentence for both — so a
    /// learner doing a cheap three-minute rep was told their new work was waiting on a *dissolving
    /// foundation*. Nothing was dissolving. The card's reasons ARE the product: a wrong one is a lie
    /// told to someone about their own mind, and they have no way to check it.
    [Fact]
    public void A_rep_does_not_borrow_the_rescues_excuse()
    {
        var rep = TodayScheduler.Choose(new SchedulerInputs(
            15, [Due("cheap", 0.85, minutes: 3)], Step("forward", 30)));

        Assert.Equal(CardKind.Rep, rep.Kind);

        var deferred = rep.AlsoDue.Single(d => d.Slug == "forward");

        Assert.Contains("it will keep", deferred.Reason, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "ground under it is solid", deferred.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reps_can_never_consume_more_than_their_share_of_a_session()
    {
        // Sweep every session length: no rep is ever chosen if it would eat more than the cap.
        foreach (var session in new[] { 5, 10, 15, 25 })
        {

            var card = TodayScheduler.Choose(new SchedulerInputs(
                session,
                [Due("greedy", 0.85, minutes: session)],   // a rep as long as the whole session
                Step("forward", 30)));

            Assert.True(
                card.Kind != CardKind.Rep,
                $"In a {session}-minute session the scheduler chose a {session}-minute rep. The "
                + "learner is now revising instead of learning, and will keep doing so forever.");
        }
    }

    /// **Rescue is the exception, and it must be.** A foundation actually dissolving beats the cap,
    /// because building on it is worse than not building at all.
    [Fact]
    public void A_rescue_is_allowed_to_take_the_whole_session()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(
            SessionMinutes: 10,
            Due: [Due("dissolving", 0.25, minutes: 30)],
            NextStep: Step("forward", 30)));

        Assert.Equal(CardKind.Rescue, card.Kind);

        // Capped to what they have — they will finish it next session, and that is fine. What is not
        // fine is building on it in the meantime.
        Assert.Equal(10, card.Minutes);
    }

    // ── 3. Move forward ──────────────────────────────────────────────────────

    [Fact]
    public void With_nothing_rotting_the_learner_moves_forward()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(25, [], Step("hnsw", 90, position: 9)));

        Assert.Equal(CardKind.Learn, card.Kind);
        Assert.Equal("hnsw", card.Slug);
        Assert.Contains("Step 9 of your plan", card.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void A_concept_they_let_rot_is_rebuilt_not_pretended_finished()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(
            25, [], Step("bm25", 40, kind: StepKind.Refresh)));

        Assert.Equal(CardKind.Refresh, card.Kind);
        Assert.Contains("You held this once", card.Headline, StringComparison.Ordinal);
        Assert.Contains("it is not free", card.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_never_asks_for_more_time_than_the_learner_has()
    {
        // A 90-minute concept in a 15-minute session. They start it; they do not have to finish it.
        var card = TodayScheduler.Choose(new SchedulerInputs(15, [], Step("retrieval", 90)));

        Assert.Equal(15, card.Minutes);
    }

    // ── 4. Nothing is a real answer ──────────────────────────────────────────

    /// **PRAXIS optimises for the learner leaving.**
    ///
    /// No invented quiz. No streak to protect. An empty day is a real state, and it gets said out
    /// loud — which is exactly the thing a product measured on engagement could never ship.
    [Fact]
    public void When_there_is_nothing_to_do_PRAXIS_says_so_instead_of_inventing_work()
    {
        var card = TodayScheduler.Choose(new SchedulerInputs(25, [], NextStep: null));

        Assert.Equal(CardKind.Nothing, card.Kind);
        Assert.Equal(0, card.Minutes);
        Assert.Empty(card.AlsoDue);

        Assert.Contains("will not invent some", card.Because, StringComparison.Ordinal);
        Assert.Contains("the point was never the app", card.Because, StringComparison.Ordinal);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    /// A scheduler that says something different each time you look at it is not scheduling. It is
    /// stalling.
    [Fact]
    public void The_same_learner_gets_the_same_card()
    {
        var inputs = new SchedulerInputs(
            15,
            [Due("a", 0.75, 3), Due("b", 0.82, 4)],
            Step("c", 45));

        var first = TodayScheduler.Choose(inputs);
        var second = TodayScheduler.Choose(inputs);

        // Compared field by field rather than with record equality: `AlsoDue` is a list, and C#
        // records compare list members by *reference*, so two identical cards would come back
        // unequal for a reason that has nothing to do with scheduling.
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.ConceptId, second.ConceptId);
        Assert.Equal(first.Minutes, second.Minutes);
        Assert.Equal(first.Headline, second.Headline);
        Assert.Equal(first.Because, second.Because);

        Assert.Equal(
            first.AlsoDue.Select(d => (d.ConceptId, d.Reason)),
            second.AlsoDue.Select(d => (d.ConceptId, d.Reason)));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static DueConcept Due(string slug, double retrievability, int minutes = 3)
        => new(Guid.CreateVersion7(), slug, retrievability, minutes);

    private static PlanStep Step(
        string slug, double minutes, int position = 1, StepKind kind = StepKind.Learn)
        => new(Guid.CreateVersion7(), slug, position, minutes, minutes, 1, kind);
}
