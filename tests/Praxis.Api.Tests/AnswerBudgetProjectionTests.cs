using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;

namespace Praxis.Api.Tests;

/// **The budget is a projection, and that is what makes it honest.**
///
/// It is not a counter somebody decrements. It is derived by replaying the log — so the only way to
/// change what a learner has spent is to **append an event saying they spent it**, where the Twin
/// will see it and discount the evidence that follows (I-5), and the Ledger will keep it forever.
///
/// There is no other door. No admin endpoint, no support tool, no quiet UPDATE after a bad week.
[Collection(SharedApi.Name)]
public sealed class AnswerBudgetProjectionTests
{
    private readonly ApiFixture _api;

    public AnswerBudgetProjectionTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Taking_hints_spends_the_budget_through_the_real_projection()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, concept, HintRung.Probe);          // 6
        await HintAsync(learner, concept, HintRung.WorkedExample);  // 28

        var budget = await BudgetAsync(learner, concept);

        Assert.Equal(34, budget.Spent);
        Assert.Equal(86, budget.Remaining);
        Assert.Equal(HintRung.WorkedExample, budget.DeepestRung);
        Assert.Equal(2, budget.HintsTaken);
    }

    /// **The event says how much is left. The projection does not believe it.**
    ///
    /// `HintTaken.BudgetRemaining` is the *writer's* belief at the time — useful when auditing a
    /// disagreement, worthless as truth. If a buggy tutor claimed the learner had 99 hints left, and
    /// the projection took its word for it, the budget would be whatever the most broken component
    /// last asserted.
    [Fact]
    public async Task A_lying_event_cannot_grant_a_learner_extra_hints()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        // Five events, each claiming — falsely — that the learner still has a full purse.
        for (var i = 0; i < 5; i++)
        {
            await HintAsync(learner, concept, HintRung.PartialScaffold, claimsRemaining: 99);
        }

        var budget = await BudgetAsync(learner, concept);

        Assert.True(
            budget.Remaining <= AnswerBudget.DefaultAllowance,
            $"An event claimed 99 hints remained and the projection believed it (remaining: "
            + $"{budget.Remaining}). The budget is now whatever the most broken writer last said.");

        // Two scaffolds (45 each) fit inside 120; the third does not, and is refused rather than
        // clamped.
        Assert.Equal(90, budget.Spent);
        Assert.True(budget.Spent <= budget.Allowance);
    }

    /// **The standing proof that the log is the truth** (M1.6).
    ///
    /// Throw the budgets away, replay history from zero, and the same numbers must come back. If
    /// they do not, some belief about a learner is being kept somewhere history cannot audit.
    [Fact]
    public async Task The_budget_survives_being_destroyed_and_rebuilt_from_history()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, concept, HintRung.Probe);
        await HintAsync(learner, concept, HintRung.WorkedExample);

        var before = await BudgetAsync(learner, concept);

        var spent = before.Spent;
        var deepest = before.DeepestRung;

        // Destroy every projection and recompute all of them from the event log.
        await using (var scope = _api.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().RebuildAsync();
        }

        var after = await BudgetAsync(learner, concept);

        Assert.Equal(spent, after.Spent);
        Assert.Equal(deepest, after.DeepestRung);
    }

    [Fact]
    public async Task Budgets_are_per_concept_so_help_on_one_does_not_cost_you_on_another()
    {
        var learner = Guid.CreateVersion7();
        var (bm25, hnsw) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, bm25, HintRung.PartialScaffold);

        var spentOne = await BudgetAsync(learner, bm25);
        var untouched = await _api.Factory.Services.CreateAsyncScope().ServiceProvider
            .GetRequiredService<TutorDbContext>()
            .Budgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.LearnerId == learner && b.ConceptId == hnsw);

        Assert.Equal(45, spentOne.Spent);

        // No budget exists for the concept they never asked about — and when one does, it is full.
        Assert.Null(untouched);
    }

    [Fact]
    public async Task One_learners_spending_never_touches_anothers()
    {
        var concept = Guid.CreateVersion7();
        var (alice, bob) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(alice, concept, HintRung.PartialScaffold);

        var hers = await BudgetAsync(alice, concept);

        Assert.Equal(45, hers.Spent);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TutorDbContext>();

        Assert.Null(await db.Budgets.FirstOrDefaultAsync(b => b.LearnerId == bob));
    }

    // ── Production earns it back ─────────────────────────────────────────────

    /// **"Hints cost. Production earns."** (CANON §1.1)
    ///
    /// The learner takes a scaffold, then teaches the idea back. The scaffold cost them 45; the
    /// teach-back earns 25. That is the whole economy: help is expensive, and *making something* is
    /// how you pay for it.
    [Fact]
    public async Task Producing_something_buys_back_budget_through_the_real_projection()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, concept, HintRung.PartialScaffold);   // −45

        Assert.Equal(75, (await BudgetAsync(learner, concept)).Remaining);

        await ProduceAsync(learner, concept, ProductionKind.TeachBack);   // +25

        var budget = await BudgetAsync(learner, concept);

        Assert.Equal(100, budget.Remaining);
        Assert.Equal(25, budget.Earned);
        Assert.Equal(1, budget.ProductionsRecorded);
    }

    /// **PRAXIS pays you to fail properly.**
    ///
    /// A real, reasoned, *wrong* answer earns budget. It has to: the moment failure is free but
    /// unpaid, the rational move is to stop trying and wait for a hint.
    [Fact]
    public async Task A_genuine_wrong_attempt_earns_budget_through_the_real_projection()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, concept, HintRung.Probe);   // −6

        await ProduceAsync(learner, concept, ProductionKind.GenuineWrongAttempt);   // +8

        var budget = await BudgetAsync(learner, concept);

        Assert.Equal(8, budget.Earned);

        // They asked for a hint, got it wrong, *thought about it* — and are better off than they
        // started. That is not generosity; it is the only rule under which a learner keeps trying.
        Assert.True(budget.Remaining > AnswerBudget.DefaultAllowance - 6);
    }

    /// The event says **what the learner made**. The projection decides **what it was worth**. A
    /// tutor that could name its own price would eventually name a generous one.
    [Fact]
    public async Task Earning_survives_being_destroyed_and_rebuilt_from_history()
    {
        var (learner, concept) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        await HintAsync(learner, concept, HintRung.Analogy);
        await ProduceAsync(learner, concept, ProductionKind.SelfExplanation);
        await ProduceAsync(learner, concept, ProductionKind.CaughtOwnError);

        var before = await BudgetAsync(learner, concept);

        await using (var scope = _api.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().RebuildAsync();
        }

        var after = await BudgetAsync(learner, concept);

        Assert.Equal(before.Spent, after.Spent);
        Assert.Equal(before.Earned, after.Earned);
        Assert.Equal(before.Remaining, after.Remaining);

        Assert.Equal(35, after.Earned);   // 20 + 15
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private async Task ProduceAsync(Guid learner, Guid concept, ProductionKind kind)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var appended = await store.AppendAsync(
            learner,
            new ProductionRecorded(Guid.CreateVersion7(), kind),
            DateTime.UtcNow,
            concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private async Task HintAsync(Guid learner, Guid concept, HintRung rung, int claimsRemaining = 0)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var appended = await store.AppendAsync(
            learner,
            new HintTaken(Guid.CreateVersion7(), rung, claimsRemaining),
            DateTime.UtcNow,
            concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private async Task<AnswerBudget> BudgetAsync(Guid learner, Guid concept)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TutorDbContext>();

        return await db.Budgets
            .AsNoTracking()
            .SingleAsync(b => b.LearnerId == learner && b.ConceptId == concept);
    }
}
