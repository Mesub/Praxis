using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Features.Diagnostics;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **The loop that is the product.**
///
/// A learner gets something wrong. PRAXIS does not merely record that they were wrong — it works out
/// **what they must believe** for that answer to have been the reasonable thing to write, names it,
/// and then **goes hunting for it with the very next question.**
///
/// That is the difference between an app that marks your homework and a system that models your mind.
/// Everything in M7 exists to make this one sequence true, and this file is the only place it is
/// proved end to end.
[Collection(SharedAssessment.Name)]
public sealed class TheHuntTests
{
    private const string TheBelief = "M-SR-004";

    private readonly AssessmentFixture _fixture;

    public TheHuntTests(AssessmentFixture fixture) => _fixture = fixture;

    /// **The headline.**
    ///
    /// One wrong answer, and the next thing PRAXIS asks is not *"here is a slightly harder question"*
    /// — it is *"let me see whether you really believe what I think you believe."*
    [Fact]
    public async Task A_wrong_answer_produces_a_named_misconception_and_the_next_item_targets_it()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        // A bank with two items PRAXIS could ask next. The *general* one is the more informative of
        // the two — it is a sharper instrument for measuring theta, and a plain CAT would take it
        // every time.
        var asked = await world.Bank(concept, difficulty: 0.0, discrimination: 1.0);

        var general = await world.Bank(concept, difficulty: 0.1, discrimination: 1.8);

        var hunts = await world.Bank(
            concept, difficulty: 0.1, discrimination: 1.0, targets: TheBelief);

        // ── They get it wrong, and the oracle names the belief behind it ─────
        world.Oracle.Says(ClaimOutcome.Wrong, misconception: TheBelief);

        var answered = await world.Submit(new SubmitAnswer.Command(
            SessionId: Guid.CreateVersion7(),
            ItemId: asked,
            Claim: "Support is a floor. Price physically can't go below it.",
            StatedConfidence: 0.9,
            Seconds: 30));

        Assert.True(answered.IsSuccess);

        // **A named belief, not a note that they were wrong.**
        Assert.Equal(TheBelief, answered.Value.Misconception);

        // And PRAXIS says the one sentence a learner cannot get anywhere else.
        Assert.True(answered.Value.YouSaidYouWereSure);
        Assert.Equal("not", answered.Value.AndYouWere);

        // ── It is on the ledger, by name ─────────────────────────────────────
        var mc = await world.Misconception(concept);

        Assert.Equal(TheBelief, mc.Key);
        Assert.Equal(MisconceptionStatus.Suspected, mc.Status);

        // Their own words, kept for the human who will eventually read them.
        Assert.Contains("physically can't go below", mc.RevealedBy, StringComparison.Ordinal);

        // ── And now the hunt ─────────────────────────────────────────────────
        var next = await world.Next(concept);

        Assert.True(next.IsSuccess);
        Assert.False(next.Value.Stopped);

        Assert.Equal(
            hunts.ToString(),
            next.Value.ItemId);

        Assert.NotEqual(general.ToString(), next.Value.ItemId);
    }

    /// **And the price of the hunt is real, which is why it must be bounded.**
    ///
    /// PRAXIS pays a *slight* premium to resolve an open misconception (I-6). It does not pay **any**
    /// price — at that point it has stopped assessing and is just following a hunch on somebody's
    /// afternoon. A drastically better item still wins.
    [Fact]
    public async Task The_hunt_pays_a_premium_and_not_a_blank_cheque()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        var asked = await world.Bank(concept, difficulty: 0.0, discrimination: 1.0);

        // A *far* more informative item, aimed squarely at the decision.
        var excellent = await world.Bank(concept, difficulty: 0.0, discrimination: 2.5);

        // A near-useless item that happens to touch the misconception. It cannot buy its way in on
        // the strength of that alone.
        await world.Bank(concept, difficulty: 3.5, discrimination: 0.3, targets: TheBelief);

        world.Oracle.Says(ClaimOutcome.Wrong, misconception: TheBelief);

        await world.Submit(new SubmitAnswer.Command(
            Guid.CreateVersion7(), asked, "Wrong, in a diagnosable way.", 0.8, 30));

        var next = await world.Next(concept);

        Assert.Equal(excellent.ToString(), next.Value.ItemId);
    }

    /// **A right answer is not a misconception.**
    ///
    /// A misconception engine that always finds a misconception is a horoscope.
    [Fact]
    public async Task Getting_it_right_names_no_wrong_belief()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        var item = await world.Bank(concept, difficulty: 0.0);

        world.Oracle.Says(ClaimOutcome.Correct);

        var answered = await world.Submit(new SubmitAnswer.Command(
            Guid.CreateVersion7(), item, "Resting limit orders absorb arriving flow.", 0.8, 40));

        Assert.Null(answered.Value.Misconception);

        Assert.Empty(await world.Db.Misconceptions.Where(m => m.ConceptId == concept).ToListAsync());

        // And what PRAXIS believes about them moved — derived, not decided.
        var ability = await world.Db.Abilities.AsNoTracking()
            .SingleAsync(a => a.LearnerId == world.Learner && a.ConceptId == concept);

        Assert.True(ability.Theta > Ability.Unknown.Theta);
    }

    /// **Sometimes a person is just wrong**, with no coherent wrong rule underneath it — a slip, a
    /// misread, a bad afternoon. The oracle says so by naming nothing, and PRAXIS does not invent a
    /// belief to teach them out of.
    [Fact]
    public async Task A_wrong_answer_with_no_coherent_belief_behind_it_invents_nothing()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        var item = await world.Bank(concept, difficulty: 0.0);

        world.Oracle.Says(ClaimOutcome.Wrong, misconception: null);

        var answered = await world.Submit(new SubmitAnswer.Command(
            Guid.CreateVersion7(), item, "erm, no idea", 0.2, 5));

        Assert.Null(answered.Value.Misconception);

        Assert.Empty(await world.Db.Misconceptions.Where(m => m.ConceptId == concept).ToListAsync());
    }

    /// **PRAXIS does not guess, and it especially does not default to WRONG.**
    ///
    /// A learner marked wrong because a service was down would carry that in their Twin forever, and
    /// would have no way of ever finding out why.
    [Fact]
    public async Task When_the_oracle_cannot_be_reached_nothing_is_written_about_the_learner()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        var item = await world.Bank(concept, difficulty: 0.0);

        world.Oracle.Fails();

        var answered = await world.Submit(new SubmitAnswer.Command(
            Guid.CreateVersion7(), item, "A perfectly good answer.", 0.8, 40));

        Assert.False(answered.IsSuccess);

        // **Nothing.** No answer, no ability, no misconception.
        Assert.Empty(await world.Db.Responses.Where(r => r.ConceptId == concept).ToListAsync());
        Assert.Empty(await world.Db.Abilities.Where(a => a.ConceptId == concept).ToListAsync());
    }

    /// **"Tiredness is not ignorance."**
    ///
    /// A learner who was getting things right half an hour ago is now getting them wrong, and taking
    /// twice as long about it. A machine that reads that as *"they do not know this"* will tear up
    /// their plan and inject prerequisites they do not need — **because they had a long day** — and
    /// the Twin will be wrong about a person because they were tired on a Tuesday.
    ///
    /// So PRAXIS stops. **And stopping is a success, not an error**: nothing went wrong, and the
    /// learner has not failed.
    ///
    /// **This test found a real bug.** `RecentWindow` was 5, and `IsTiring` needs at least 6 attempts
    /// to see a shape — so the check could never fire through this slice. Nothing went red. The
    /// refusal was simply never reached, which is the worst way for a guarantee to die.
    [Fact]
    public async Task A_tired_learner_is_sent_home_rather_than_marked_down()
    {
        var world = World();
        var concept = Guid.CreateVersion7();

        // Half an hour ago they were fine: right, and quick about it.
        for (var i = 0; i < 3; i++)
        {
            var item = await world.Bank(concept, difficulty: 0.0);

            world.Oracle.Says(ClaimOutcome.Correct);

            await world.Submit(new SubmitAnswer.Command(
                Guid.CreateVersion7(), item, "Right, and quickly.", 0.7, 40));
        }

        // And now they are not: wrong, and slow.
        for (var i = 0; i < 3; i++)
        {
            var item = await world.Bank(concept, difficulty: 0.0);

            world.Oracle.Says(ClaimOutcome.Wrong);

            await world.Submit(new SubmitAnswer.Command(
                Guid.CreateVersion7(), item, "Wrong, and slowly.", 0.4, 200));
        }

        await world.Bank(concept, difficulty: 0.0);

        var next = await world.Next(concept);

        Assert.True(next.IsSuccess, "Stopping is not an error. The learner has not failed.");
        Assert.True(next.Value.Stopped);
        Assert.Null(next.Value.ItemId);

        Assert.Contains("tiredness, not ignorance", next.Value.Says!, StringComparison.Ordinal);
        Assert.Contains("no penalty", next.Value.Says!, StringComparison.OrdinalIgnoreCase);

        // **And it does not claim certainty it does not have.** Zeroing the standard error out
        // because there is no item would be a claim of *perfect knowledge* — the precise lie that
        // field exists to prevent.
        Assert.True(next.Value.StandardError > 0);
    }

    /// **PRAXIS will not invent an item to keep the session moving.** A made-up item produces made-up
    /// evidence, and made-up evidence about a person's mind is the one thing this system exists to
    /// refuse.
    [Fact]
    public async Task An_empty_bank_is_said_out_loud_and_not_papered_over()
    {
        var world = World();

        var next = await world.Next(Guid.CreateVersion7());

        Assert.False(next.IsSuccess);
        Assert.Contains("will not invent one", next.Message!, StringComparison.Ordinal);
    }

    /// **The learner is never shown the answer, because there is nowhere for one to be.**
    ///
    /// Not filtered. Not redacted. There is no field on either response record that could hold one.
    [Fact]
    public void No_response_the_learner_receives_has_anywhere_to_put_an_answer()
    {
        foreach (var type in new[] { typeof(NextItemResponse), typeof(AnswerResponse) })
        {
            var fields = type.GetProperties().Select(p => p.Name).ToList();

            foreach (var leak in new[] { "Answer", "Solution", "Expected", "Key", "Explanation" })
            {
                Assert.DoesNotContain(
                    fields,
                    f => f.Contains(leak, StringComparison.OrdinalIgnoreCase)
                         && f != "AnswerResponse");
            }
        }
    }

    // ── The world ────────────────────────────────────────────────────────────

    private Harness World() => new(_fixture);

    private sealed class Harness
    {
        public AssessmentDbContext Db { get; }

        public FakeOracle Oracle { get; } = new();

        /// A fresh learner per test. **The event log is append-only and shared**, which is exactly how
        /// it is in production — so the tests do not get to reset it, they get to be about a person
        /// nobody has met.
        public Guid Learner { get; } = Guid.CreateVersion7();

        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly FixedClock _clock = new();

        public Harness(AssessmentFixture fixture)
        {
            Db = fixture.CreateContext();

            var kernel = fixture.CreateKernel();

            // **The real event store, and the real runner.** A fake would let me drive the
            // projections by hand, in the order I chose — which is the exact thing that would hide
            // the bug. The claim is that a wrong answer becomes a named belief *because it was
            // written to the log and read back out*.
            _events = new EventStore(kernel, _clock);

            _projections = new ProjectionRunner(
                _events,
                kernel,
                _clock,
                NullLogger<ProjectionRunner>.Instance,
                [new AbilityProjection(Db), new MisconceptionProjection(Db)]);
        }

        public async Task<Guid> Bank(
            Guid concept,
            double difficulty,
            double discrimination = 1.0,
            string? targets = null)
        {
            var id = Guid.CreateVersion7();

            Db.Items.Add(BankedItem.From(
                id,
                concept,
                EvidenceType.Apply,
                stem: "Why does price react at a level?",
                fingerprint: id.ToString("N"),
                difficulty: difficulty,
                discrimination: discrimination,
                targetsMisconception: targets));

            await Db.SaveChangesAsync();

            Db.ChangeTracker.Clear();

            return id;
        }

        public Task<Result<AnswerResponse>> Submit(SubmitAnswer.Command command)
        {
            // Each answer a minute later than the last, so `Adaptation` sees them in the order they
            // happened rather than all at once.
            _clock.Advance(TimeSpan.FromMinutes(1));

            return new SubmitAnswer.Handler(
                    Db, _events, _projections, Oracle, new FakeUser(Learner), _clock)
                .HandleAsync(command, default);
        }

        public Task<Result<NextItemResponse>> Next(Guid concept)
        {
            Db.ChangeTracker.Clear();

            return new NextItem.Handler(Db, new FakeUser(Learner))
                .HandleAsync(new NextItem.Query(Guid.CreateVersion7(), concept), default);
        }

        public async Task<LearnerMisconception> Misconception(Guid concept)
        {
            Db.ChangeTracker.Clear();

            return await Db.Misconceptions.SingleAsync(
                m => m.LearnerId == Learner && m.ConceptId == concept);
        }
    }
}
