using Praxis.Infrastructure.Ai;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Memory;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Memory.Features.Reps;
using Praxis.Shared;

namespace Praxis.Modules.Memory.Tests;

/// **A rep is not special**, and "not special" is doing a lot of work.
///
/// The temptation here is enormous and entirely reasonable-sounding: the learner has just proved they
/// still know something, so surely it should count for *something*. It counts for exactly what it is
/// evidence of — **that the memory is still there** — and not one thing more.
public sealed class ARepIsNotSpecialTests
{
    private static readonly Guid Concept = Guid.CreateVersion7();
    private static readonly Guid Learner = Guid.CreateVersion7();

    // ── It cannot manufacture mastery ────────────────────────────────────────

    /// **The load-bearing one.**
    ///
    /// If a rep could confer mastery, a learner could reach `HOLD` by answering an endless stream of
    /// cheap questions about something they had already proved — and the Evidence Ceiling (C-4), the
    /// load-bearing wall of this entire product, would have a door in it marked *"review"*.
    ///
    /// A rep tells you the memory is still there. **It does not tell you they can *do* anything**, and
    /// only the second thing is mastery.
    ///
    /// The refusal is enforced in the Twin's projection (`OnRepAsync` passes `masteryCredit` and
    /// `isHeld` through untouched) and asserted here, on the response the learner actually receives.
    [Fact]
    public async Task Passing_a_rep_does_not_move_mastery_and_the_response_says_so()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Correct);

        var answered = await world.Rep(confidence: 0.8);

        Assert.True(answered.IsSuccess);
        Assert.True(answered.Value.Correct);

        Assert.True(
            answered.Value.MasteryUnchanged,
            "A rep just moved mastery. The Evidence Ceiling now has a door in it marked 'review', "
            + "and a learner can reach HOLD by answering cheap questions about something they had "
            + "already proved.");
    }

    /// **Not even a hundred of them.** There is no accumulation path, because there is no path.
    [Fact]
    public async Task A_hundred_passed_reps_do_not_move_mastery_either()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Correct);

        for (var i = 0; i < 100; i++)
        {
            var answered = await world.Rep(confidence: 0.9);

            Assert.True(answered.Value.MasteryUnchanged);
        }

        // And the events landed. This is not passing because nothing happened.
        Assert.Equal(100, world.Events.Appended.Count);
        Assert.All(world.Events.Appended, e => Assert.IsType<RepAnswered>(e));
    }

    // ── It goes on the log like anything else ────────────────────────────────

    /// A rep is an event, appended to the same log, and the Twin recomputes from it. **It does not get
    /// a private table, a private grader, or a private path into the learner's model.**
    [Fact]
    public async Task A_rep_is_recorded_on_the_event_log_like_any_other_evidence()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Correct);

        await world.Rep(confidence: 0.6);

        var rep = Assert.IsType<RepAnswered>(Assert.Single(world.Events.Appended));

        Assert.True(rep.Correct);
        Assert.Equal(0.6, rep.StatedConfidence, tolerance: 1e-9);
    }

    /// **The event carries what the writer believed, and the projection ignores it.**
    ///
    /// `StabilityBefore`/`StabilityAfter` are on the payload for the audit trail — so a disagreement
    /// between the writer and the Twin is *visible* rather than silent. They are not the truth. The
    /// Twin recomputes stability from the memory model, exactly as the Answer Budget recomputes what
    /// was spent (M6.1): **a writer that can name its own price will eventually name a generous one.**
    [Fact]
    public async Task The_writers_opinion_of_the_new_stability_is_recorded_and_not_trusted()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Correct);

        await world.Rep(confidence: 0.7);

        var rep = Assert.IsType<RepAnswered>(Assert.Single(world.Events.Appended));

        // It is written down...
        Assert.True(rep.StabilityAfter > rep.StabilityBefore);

        // ...and it is exactly what the model says, so there is nothing for the Twin to disagree with
        // today. The point is that the Twin does not *have* to agree — it recomputes.
        var expected = Fsrs.Next(
            new MemoryState(Stability, Difficulty), Grading.Of(true), DaysSinceReview);

        Assert.Equal(expected.Stability, rep.StabilityAfter, tolerance: 1e-9);
    }

    // ── The oracle, and no back door ─────────────────────────────────────────

    /// **PRAXIS does not guess, and it does not default to WRONG.**
    ///
    /// A learner whose memory was marked as lapsed because a service was down would have their entire
    /// schedule rebuilt around a fiction — and they would never find out why.
    [Fact]
    public async Task When_the_oracle_is_unreachable_nothing_is_written_about_the_memory()
    {
        var world = World();

        world.Oracle.Fails();

        var answered = await world.Rep(confidence: 0.8);

        Assert.False(answered.IsSuccess);
        Assert.Empty(world.Events.Appended);
    }

    /// **A review queue is exactly where a "just show me the answer" button would feel most
    /// reasonable** — which is exactly why there is no field for one.
    [Fact]
    public void Nothing_the_learner_gets_back_from_a_rep_has_room_for_an_answer()
    {
        var fields = typeof(RepAnsweredResponse).GetProperties().Select(p => p.Name).ToList();

        foreach (var leak in new[] { "Answer", "Solution", "Expected", "Correction", "Explanation" })
        {
            Assert.DoesNotContain(fields, f => f.Contains(leak, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── The sentence a rep exists to produce ─────────────────────────────────

    /// **"You were sure, and it had gone."**
    ///
    /// This is the single most useful thing that happens in PRAXIS, and it is *only sayable* because
    /// the confidence was captured **before** the learner was told (C-6). Asked afterwards, it
    /// measures hindsight and nothing else — while still producing a number that looks exactly like a
    /// confidence.
    [Fact]
    public async Task The_most_useful_sentence_in_the_product_is_sayable_because_we_asked_first()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Wrong);

        var answered = await world.Rep(confidence: 0.95);

        Assert.False(answered.Value.Correct);

        Assert.Contains("You were sure", answered.Value.Says, StringComparison.Ordinal);
        Assert.Contains("it had gone", answered.Value.Says, StringComparison.Ordinal);
    }

    /// **"Stop revising this one — that time is yours."** Good news that is also actionable (I-10).
    [Fact]
    public async Task Being_right_when_you_thought_you_had_lost_it_gives_you_your_time_back()
    {
        var world = World();

        world.Oracle.Says(ClaimOutcome.Correct);

        var answered = await world.Rep(confidence: 0.2);

        Assert.Contains("Stop revising this one", answered.Value.Says, StringComparison.Ordinal);
    }

    /// **You cannot review something you never proved.** That is not a rep — it is learning, and it
    /// belongs to the plan.
    [Fact]
    public async Task You_cannot_rep_something_you_never_learned()
    {
        var world = World(holds: false);

        var answered = await world.Rep(confidence: 0.5);

        Assert.False(answered.IsSuccess);

        Assert.Contains(
            "not something you are behind on", answered.Message!, StringComparison.Ordinal);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private const double Stability = 20.0;
    private const double Difficulty = 5.0;
    private const double DaysSinceReview = 12.0;

    private static Harness World(bool holds = true) => new(holds);

    private sealed class Harness
    {
        public FakeOracle Oracle { get; } = new();

        public RecordingEventStore Events { get; } = new();

        private readonly AnswerRep.Handler _handler;

        public Harness(bool holds)
        {
            var snapshot = new StubMemory(holds);

            _handler = new AnswerRep.Handler(
                Events,
                NoProjections.Runner(),
                snapshot,
                Oracle,
                new FakeUser(Learner),
                new FixedClock());
        }

        public Task<Result<RepAnsweredResponse>> Rep(double confidence)
            => _handler.HandleAsync(
                new AnswerRep.Command(
                    Concept,
                    Guid.CreateVersion7(),
                    "Resting limit orders absorb arriving flow.",
                    confidence,
                    Seconds: 20),
                default);
    }

    private sealed class StubMemory : IMemorySnapshot
    {
        private readonly bool _holds;

        public StubMemory(bool holds) => _holds = holds;

        public Task<IReadOnlyList<DueRep>> HeldByAsync(Guid learnerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DueRep>>(_holds
                ? [new DueRep(Concept, "support-resistance",
                    new MemoryState(Stability, Difficulty), DaysSinceReview)]
                : []);
    }
}
