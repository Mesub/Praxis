using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Compiler;
using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The Compiler, meeting the real Graph and the real Twin.**
///
/// Everything proved as a pure solver now runs against the actual seed pack, an actual learner, and
/// an actual database.
[Collection(SharedApi.Name)]
public sealed class PlanServiceTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    private Guid _objective;

    public PlanServiceTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var result = await loader.LoadAsync(SeedPack());

        Assert.True(result.IsSuccess || result.Status == ResultStatus.Conflict, string.Join("\n", result.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        _objective = graph.Concepts.Single(c => c.Slug == "beating-the-baseline").Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_learner_with_no_objective_gets_no_plan_and_is_told_why()
    {
        // PRAXIS cannot compile a plan towards nothing, and a product that offered one anyway would
        // be selling motion rather than progress.
        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var result = await plans.ForLearnerAsync(Guid.CreateVersion7());

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Contains("selling motion", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_learner_gets_the_whole_path_to_the_objective()
    {
        var learner = await LearnerWithObjective(minutesPerDay: 60);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var result = await plans.ForLearnerAsync(learner);

        Assert.True(result.IsSuccess, result.Message);

        var compiled = result.Value;

        // All 30 concepts of the seed pack, ending at the objective.
        Assert.Equal(30, compiled.Plan.Steps.Count);
        Assert.Equal("beating-the-baseline", compiled.Plan.Steps[^1].Slug);
        Assert.Equal("tokenisation", compiled.Plan.Steps[0].Slug);

        // And PRAXIS believes in it.
        Assert.True(compiled.Believable);
        Assert.Null(compiled.Alternative);
    }

    [Fact]
    public async Task The_plan_never_puts_a_concept_before_something_it_requires()
    {
        var learner = await LearnerWithObjective(60);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        var plan = (await plans.ForLearnerAsync(learner)).Value.Plan;
        var position = plan.Steps.ToDictionary(s => s.ConceptId, s => s.Position);

        var edges = graph.Edges
            .Where(e => e.Kind == Modules.Graph.Domain.EdgeKind.Requires)
            .ToList();

        foreach (var edge in edges.Where(e => position.ContainsKey(e.ToConceptId)))
        {
            Assert.True(
                position[edge.FromConceptId] < position[edge.ToConceptId],
                "A concept was scheduled before one of its prerequisites, in the real pack.");
        }
    }

    /// **What the learner already holds is never taught again.**
    ///
    /// The cache hit. Time is their scarcest resource, and re-teaching them something they can
    /// demonstrate is theft.
    [Fact]
    public async Task Proving_something_removes_it_from_the_plan()
    {
        var learner = await LearnerWithObjective(60);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        var before = (await plans.ForLearnerAsync(learner)).Value.Plan;
        Assert.Contains(before.Steps, s => s.Slug == "tokenisation");

        // Prove tokenisation, for real: an event, through the real projection.
        var tokenisation = graph.Concepts.Single(c => c.Slug == "tokenisation").Id;
        await ProveAsync(learner, tokenisation);

        var after = (await plans.ForLearnerAsync(learner)).Value.Plan;

        Assert.DoesNotContain(after.Steps, s => s.Slug == "tokenisation");
        Assert.Equal(before.Steps.Count - 1, after.Steps.Count);
        Assert.Equal(1, after.ConceptsAlreadyHeld);
        Assert.True(after.TotalMinutes < before.TotalMinutes);
    }

    // ── The refusal ──────────────────────────────────────────────────────────

    /// **PRAXIS looks a real learner in the eye and says no.**
    ///
    /// Not a synthetic 180-node fixture — the actual seed pack, the actual Decay Wall, at a pace a
    /// real person would offer.
    [Fact]
    public async Task At_a_thin_pace_the_objective_is_refused_and_something_honest_is_offered_instead()
    {
        // The whole 30-concept objective, on ten minutes a day, against a 90-day deadline.
        var learner = await LearnerWithObjective(
            minutesPerDay: 10,
            deadline: DateTime.UtcNow.AddDays(90));

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var compiled = (await plans.ForLearnerAsync(learner)).Value;

        Assert.False(
            compiled.Believable,
            "PRAXIS agreed to get someone from zero to beating BM25 on ten minutes a day. Every "
            + "other product in this industry would take that money.");

        Assert.NotNull(compiled.Alternative);

        // It offers a narrower objective it actually believes in...
        Assert.NotEmpty(compiled.Alternative.Steps);
        Assert.True(compiled.Alternative.ConceptsReachable < compiled.Alternative.ConceptsInObjective);

        // ...and a number, not a platitude.
        Assert.NotNull(compiled.Alternative.MinutesPerDayThatWouldWork);
        Assert.True(compiled.Alternative.MinutesPerDayThatWouldWork > 10);
    }

    /// A deadline in the past is not a hard plan — it is an impossible one, and the honest answer is
    /// not an empty roadmap with a sad face on it.
    ///
    /// **Found by a test of my own making:** I wrote a fixture with a deadline that had already
    /// passed, and PRAXIS cheerfully offered a "narrower objective" containing zero concepts.
    [Fact]
    public async Task A_deadline_in_the_past_is_refused_rather_than_answered_with_an_empty_plan()
    {
        var learner = await LearnerWithObjective(60, deadline: DateTime.UtcNow.AddDays(-30));

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var result = await plans.ForLearnerAsync(learner);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("into the past", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_objective_at_a_serious_pace_is_believed_in()
    {
        var learner = await LearnerWithObjective(90);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var compiled = (await plans.ForLearnerAsync(learner)).Value;

        Assert.True(compiled.Believable);
        Assert.True(compiled.Wall.Reachable);
    }

    // ── The nightly pass ─────────────────────────────────────────────────────

    /// **The news nobody else would send.**
    ///
    /// The learner who fell out of reach without doing anything wrong — because *nothing happened*,
    /// and everything they held decayed while they slept.
    [Fact]
    public async Task The_nightly_pass_finds_the_learner_who_is_no_longer_on_track()
    {
        var behind = await LearnerWithObjective(
            minutesPerDay: 10,
            deadline: DateTime.UtcNow.AddDays(90));

        var fine = await LearnerWithObjective(90);

        var nightly = _api.Factory.Services.GetRequiredService<NightlyRecompile>();

        var result = await nightly.RunOnceAsync();

        Assert.True(result.Recompiled >= 2);

        Assert.Contains(behind, result.FellBehind);
        Assert.DoesNotContain(fine, result.FellBehind);
    }

    [Fact]
    public async Task The_pass_records_when_it_last_told_them_where_they_stood()
    {
        var learner = await LearnerWithObjective(60);

        var nightly = _api.Factory.Services.GetRequiredService<NightlyRecompile>();
        await nightly.RunOnceAsync();

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompilerDbContext>();

        var objective = db.Objectives.Single(o => o.LearnerId == learner);

        Assert.NotNull(objective.LastCompiledAt);
        Assert.False(RecompileTrigger.IsStaleByAge(objective.LastCompiledAt, DateTime.UtcNow));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private async Task<Guid> LearnerWithObjective(int minutesPerDay, DateTime? deadline = null)
    {
        var learner = Guid.CreateVersion7();

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompilerDbContext>();

        db.Objectives.Add(Objective.Set(learner, _objective, minutesPerDay, deadline));
        await db.SaveChangesAsync();

        return learner;
    }

    private async Task ProveAsync(Guid learner, Guid concept)
    {
        // Not a fixture. A real event, through the real projection.
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();

        await store.AppendAsync(
            learner,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, Score: 1.0, Assurance: 1.0),
            DateTime.UtcNow,
            concept);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private static string SeedPack()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "ai-engineering", "pack.yaml"));
    }
}
