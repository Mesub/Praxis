using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **Everything PRAXIS believes about a learner, meeting the real event log.**
///
/// The arithmetic was proved as a pure function in M7.2–M7.5. Here it has to survive contact with
/// events arriving one at a time, out of a database — and, more importantly, it has to survive being
/// **thrown away and rebuilt from nothing**, because that is the property that makes the belief
/// auditable rather than merely stored.
[Collection(SharedAssessment.Name)]
public sealed class AssessmentProjectionTests
{
    private static readonly DateTime Day1 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly AssessmentFixture _fixture;

    public AssessmentProjectionTests(AssessmentFixture fixture) => _fixture = fixture;

    // ── The ability estimate is derived, never decided ───────────────────────

    [Fact]
    public async Task Answering_correctly_raises_what_PRAXIS_believes_and_narrows_its_doubt()
    {
        var (db, ability, _) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var item = await Bank(db, concept, difficulty: 0.0);

        await ability.ApplyAsync(Answer(learner, concept, item, correct: true));

        var estimate = await Estimate(db, learner, concept);

        Assert.True(estimate.Theta > Ability.Unknown.Theta);

        // And the doubt shrinks. **θ without its standard error is a number pretending to be
        // knowledge**, so the two move together or the estimate is a lie.
        Assert.True(estimate.StandardError < Ability.Unknown.StandardError);
    }

    /// **One answer does not make PRAXIS sure of anything.**
    ///
    /// A product that is confident after a single item will tell somebody they have a blind spot on
    /// the strength of one bad afternoon.
    [Fact]
    public async Task One_answer_leaves_PRAXIS_honestly_unsure()
    {
        var (db, ability, _) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var item = await Bank(db, concept, difficulty: 0.0);

        await ability.ApplyAsync(Answer(learner, concept, item, correct: true));

        Assert.False((await Estimate(db, learner, concept)).IsConfident);
    }

    /// **The whole belief, rebuilt from zero.**
    ///
    /// This is the kernel's one non-negotiable: the Twin is a projection of the event log and must be
    /// reconstructible from it. If a rebuild ever produced a different number, then some belief about
    /// a learner is being kept somewhere history cannot audit — and at that point it is not a
    /// projection, it is a rumour with a database table.
    [Fact]
    public async Task Throwing_the_estimate_away_and_replaying_history_produces_the_same_belief()
    {
        var (db, ability, _) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var easy = await Bank(db, concept, difficulty: -1.0);
        var middling = await Bank(db, concept, difficulty: 0.0);
        var hard = await Bank(db, concept, difficulty: 1.5);

        var history = new[]
        {
            Answer(learner, concept, easy, correct: true),
            Answer(learner, concept, middling, correct: true),
            Answer(learner, concept, hard, correct: false),
            Answer(learner, concept, middling, correct: false),
        };

        foreach (var e in history)
        {
            await ability.ApplyAsync(e);
        }

        var before = await Estimate(db, learner, concept);
        var (theta, se, responses) = (before.Theta, before.StandardError, before.Responses);

        // ── Burn it down ─────────────────────────────────────────────────────
        await ability.ResetAsync();

        db.ChangeTracker.Clear();

        Assert.Null(await db.Abilities.FirstOrDefaultAsync(
            a => a.LearnerId == learner && a.ConceptId == concept));

        // ── And replay ───────────────────────────────────────────────────────
        foreach (var e in history)
        {
            await ability.ApplyAsync(e);
        }

        var after = await Estimate(db, learner, concept);

        Assert.Equal(theta, after.Theta, tolerance: 1e-9);
        Assert.Equal(se, after.StandardError, tolerance: 1e-9);
        Assert.Equal(responses, after.Responses);
    }

    /// **The same answer, delivered twice, is still one answer.**
    ///
    /// A projection runner that redelivers on a retry would otherwise score a learner twice for one
    /// thing they did — and PRAXIS would be *more* sure of them for a reason that has nothing to do
    /// with them.
    [Fact]
    public async Task An_event_delivered_twice_does_not_count_twice()
    {
        var (db, ability, _) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var item = await Bank(db, concept, difficulty: 0.0);

        var once = Answer(learner, concept, item, correct: true);

        await ability.ApplyAsync(once);
        await ability.ApplyAsync(once);

        var estimate = await Estimate(db, learner, concept);

        Assert.Equal(1, estimate.Responses);

        db.ChangeTracker.Clear();

        Assert.Equal(1, (await db.Items.SingleAsync(i => i.Id == item)).TimesServed);
    }

    /// **Do not guess an item's difficulty.** An item assumed to be average drags every estimate
    /// toward the middle, and the learner would never find out why PRAXIS had quietly decided they
    /// were unremarkable.
    [Fact]
    public async Task An_answer_to_an_item_we_do_not_have_moves_nothing()
    {
        var (db, ability, _) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await ability.ApplyAsync(Answer(learner, concept, Guid.CreateVersion7(), correct: true));

        db.ChangeTracker.Clear();

        Assert.Null(await db.Abilities.FirstOrDefaultAsync(
            a => a.LearnerId == learner && a.ConceptId == concept));
    }

    // ── The misconception ledger ─────────────────────────────────────────────

    /// One hit is a suspicion. **PRAXIS does not spend a learner's evenings on a suspicion.**
    [Fact]
    public async Task One_wrong_answer_is_a_suspicion_and_not_a_diagnosis()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004"));

        var mc = await Misconception(db, learner, concept);

        Assert.Equal(MisconceptionStatus.Suspected, mc.Status);
        Assert.Equal(1, mc.IndependentHits);
    }

    [Fact]
    public async Task Two_independent_hits_is_a_diagnosis()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004"));
        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004", at: Day1.AddDays(2)));

        Assert.Equal(MisconceptionStatus.Confirmed, (await Misconception(db, learner, concept)).Status);
    }

    /// **A suspicion PRAXIS never lets go of is a suspicion that becomes a fact by attrition.**
    ///
    /// Three clean answers on items built to catch this belief, and it is dropped entirely. An engine
    /// that keeps every hunch open until it is eventually right is not being careful — it is being
    /// unfalsifiable.
    [Fact]
    public async Task A_suspicion_that_does_not_survive_the_evidence_is_dropped()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004"));

        var trap = await Bank(db, concept, difficulty: 0.0, targets: "M-SR-004");

        for (var i = 0; i < MisconceptionLifecycle.CleanItemsToDismiss; i++)
        {
            await misconceptions.ApplyAsync(
                Answer(learner, concept, trap, correct: true, at: Day1.AddDays(i + 1)));
        }

        db.ChangeTracker.Clear();

        Assert.Null(await db.Misconceptions.FirstOrDefaultAsync(
            m => m.LearnerId == learner && m.ConceptId == concept));
    }

    /// **Getting a general question right proves nothing about a specific wrong belief.**
    ///
    /// They may simply not have walked into the trap this time. Only an item whose distractors were
    /// built to catch *this* belief says anything about it — and that distinction is the whole
    /// difference between a misconception engine and a horoscope.
    [Fact]
    public async Task A_general_item_says_nothing_about_a_specific_wrong_belief()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004"));

        var general = await Bank(db, concept, difficulty: 0.0, targets: null);

        for (var i = 0; i < 10; i++)
        {
            await misconceptions.ApplyAsync(
                Answer(learner, concept, general, correct: true, at: Day1.AddDays(i + 1)));
        }

        var mc = await Misconception(db, learner, concept);

        Assert.Equal(MisconceptionStatus.Suspected, mc.Status);
        Assert.Equal(0, mc.CleanItemsSince);
        Assert.Equal(0, mc.TargetedItemsPassed);
    }

    /// **Three targeted items in one evening is one evening.** *One good day is not a repair.*
    [Fact]
    public async Task Passing_three_targeted_items_in_a_single_session_is_one_session()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var session = Guid.CreateVersion7();

        await Confirm(misconceptions, learner, concept, "M-SR-004");

        var trap = await Bank(db, concept, difficulty: 0.0, targets: "M-SR-004");

        for (var i = 0; i < 3; i++)
        {
            await misconceptions.ApplyAsync(Answer(
                learner, concept, trap, correct: true, at: Day1.AddMinutes(i), session: session));
        }

        var mc = await Misconception(db, learner, concept);

        Assert.Equal(3, mc.TargetedItemsPassed);
        Assert.Equal(1, mc.DistinctSessions);

        // And it is nowhere near eradicated. **Being told you are wrong is not evidence that you
        // have stopped being wrong.**
        Assert.False(mc.Evidence.IsEarned);
        Assert.NotEmpty(mc.Evidence.StillMissing());
    }

    /// **A relapse goes straight back to `Confirmed`, not back to the bottom of the ladder.**
    ///
    /// It is the same old belief, and PRAXIS already knows its name.
    [Fact]
    public async Task A_belief_that_comes_back_is_the_same_belief()
    {
        var (db, _, misconceptions) = Module();
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await Confirm(misconceptions, learner, concept, "M-SR-004");

        var trap = await Bank(db, concept, difficulty: 0.0, targets: "M-SR-004");

        // A run of receipts, then a relapse.
        await misconceptions.ApplyAsync(Answer(
            learner, concept, trap, correct: true, at: Day1.AddDays(3),
            evidence: EvidenceType.Critique));

        var earned = await Misconception(db, learner, concept);

        Assert.True(earned.TargetedItemsPassed > 0);
        Assert.Equal(MisconceptionStatus.Challenged, earned.Status);

        await misconceptions.ApplyAsync(Detected(learner, concept, "M-SR-004", at: Day1.AddDays(40)));

        var relapsed = await Misconception(db, learner, concept);

        Assert.Equal(MisconceptionStatus.Confirmed, relapsed.Status);

        // **And the receipts are gone.** They were evidence the belief had left; it hasn't.
        Assert.Equal(0, relapsed.TargetedItemsPassed);
        Assert.False(relapsed.Evidence.IsEarned);
    }

    /// **There is no way to close a misconception except by earning it.**
    ///
    /// Not a method, not an endpoint, not a setter. The moment somebody's *opinion* can close a wrong
    /// belief, the thing that eventually closes it will be fatigue — a long queue, a deadline, a
    /// learner who complained.
    [Fact]
    public void Nothing_on_the_ledger_lets_anybody_declare_a_belief_gone()
    {
        var methods = typeof(LearnerMisconception)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        foreach (var escape in new[] { "MarkAsFixed", "Override", "SetStatus", "Resolve", "Eradicate", "Clear" })
        {
            Assert.DoesNotContain(escape, methods);
        }

        // And the status has no public setter, so nobody can assign their way past the receipts.
        var status = typeof(LearnerMisconception).GetProperty(nameof(LearnerMisconception.Status))!;

        Assert.Null(status.SetMethod?.IsPublic == true ? status.SetMethod : null);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private (AssessmentDbContext Db, AbilityProjection Ability, MisconceptionProjection Misconceptions) Module()
    {
        var db = _fixture.CreateContext();

        return (db, new AbilityProjection(db), new MisconceptionProjection(db));
    }

    private async Task<Guid> Bank(
        AssessmentDbContext db, Guid concept, double difficulty, string? targets = null)
    {
        var id = Guid.CreateVersion7();

        db.Items.Add(BankedItem.From(
            id,
            concept,
            EvidenceType.Apply,
            stem: $"An item about {concept}.",
            fingerprint: id.ToString("N"),
            difficulty: difficulty,
            targetsMisconception: targets));

        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        return id;
    }

    /// Two independent hits — the point at which PRAXIS is willing to spend a learner's time on it.
    private static async Task Confirm(
        MisconceptionProjection projection, Guid learner, Guid concept, string key)
    {
        await projection.ApplyAsync(Detected(learner, concept, key));
        await projection.ApplyAsync(Detected(learner, concept, key, at: Day1.AddDays(1)));
    }

    private static async Task<AbilityEstimate> Estimate(
        AssessmentDbContext db, Guid learner, Guid concept)
    {
        db.ChangeTracker.Clear();

        return await db.Abilities.SingleAsync(a => a.LearnerId == learner && a.ConceptId == concept);
    }

    private static async Task<LearnerMisconception> Misconception(
        AssessmentDbContext db, Guid learner, Guid concept)
    {
        db.ChangeTracker.Clear();

        return await db.Misconceptions.SingleAsync(
            m => m.LearnerId == learner && m.ConceptId == concept);
    }

    private static LearningEvent Answer(
        Guid learner,
        Guid concept,
        Guid item,
        bool correct,
        DateTime? at = null,
        Guid? session = null,
        EvidenceType evidence = EvidenceType.Apply)
        => Event(
            learner,
            concept,
            at ?? Day1,
            new AnswerGiven(
                session ?? Guid.CreateVersion7(),
                item,
                evidence,
                correct,
                StatedConfidence: 0.6,
                Seconds: 45));

    private static LearningEvent Detected(Guid learner, Guid concept, string key, DateTime? at = null)
        => Event(
            learner,
            concept,
            at ?? Day1,
            new MisconceptionDetected(
                Guid.CreateVersion7(),
                key,
                Evidence: "They believe a price level is a physical barrier, not a finite pool of "
                          + "liquidity that gets used up."));

    private static LearningEvent Event<T>(Guid learner, Guid concept, DateTime at, T payload)
        where T : IEventPayload
        => LearningEvent.Create(learner, T.EventType, EventCatalogue.Serialize(payload), at, at, concept);
}
