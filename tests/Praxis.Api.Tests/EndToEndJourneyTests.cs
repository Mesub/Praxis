using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The end-to-end proof (M13.3) — that Phase 0 is one product, not eight demos.**
///
/// Every engine has its own tests. This is the different thing: a single learner, one continuous
/// script, whose *state flows between the engines through the front door*. It is the proof that the
/// handoffs hold — that evidence the kernel records actually moves the Twin, that a Twin that now
/// holds a concept actually changes the Compiler's plan — because a system where each part passes its
/// own tests but the seams leak is a system that works only in the test suite.
///
/// **This part (M13.3a) is the front of the journey: register → objective → plan → prove the first
/// concept → watch it land in the Twin → watch the plan recompile around it.** The later thirds add
/// doing the work through to reps (b) and the Reality Loop through to the signed Ledger record (c).
///
/// ## One honest note about how evidence is produced here
///
/// Phase 0 kept the 30-concept pack and deferred the item bank (M13.1). The item-driven *diagnostic*
/// reads that bank and — correctly — refuses to invent an item when it is empty, and nothing yet
/// generates-and-banks items. So this journey produces evidence the way the rest of the system will
/// consume it once generation is wired: as an accepted-evidence fact on the immutable log (the same
/// path `NinetyDaysTests` uses). The engines downstream of the item — Twin, Compiler, Memory, Reality,
/// Ledger — are exercised through real HTTP exactly as a learner would drive them.
[Collection(SharedApi.Name)]
public sealed class EndToEndJourneyTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private Guid _objective;      // beating-the-baseline — the pack's one build target
    private Guid _frontier;       // tokenisation — the single place a learner can start

    public EndToEndJourneyTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        // Idempotent: another class in this collection may already have loaded the real pack.
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var loaded = await loader.LoadAsync(SeedPack());
        Assert.True(
            loaded.IsSuccess || loaded.Status == ResultStatus.Conflict,
            string.Join(Environment.NewLine, loaded.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        _objective = graph.Concepts.Single(c => c.Slug == "beating-the-baseline").Id;
        _frontier = graph.Concepts.Single(c => c.Slug == "tokenisation").Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task From_registration_to_a_believable_plan_that_recompiles_around_proven_evidence()
    {
        // ── Stage 1: register ────────────────────────────────────────────────
        var (client, learner) = await Register();

        // ── Stage 2: state the objective, and get the plan ───────────────────
        await SetObjective(client, minutesPerDay: 90);

        var plan = await GetPlan(client);

        // The plan is the whole 30-concept path, and it is honest: it starts where a learner who
        // knows nothing can start, and it ends at the one thing the pack exists to reach.
        Assert.True(plan.Believable, "A 90-minute-a-day plan for the seed pack should be believable.");
        Assert.Equal(30, plan.Steps.Count);
        Assert.Equal("tokenisation", plan.Steps[0].Slug);
        Assert.Equal("beating-the-baseline", plan.Steps[^1].Slug);
        Assert.Equal(0, plan.ConceptsAlreadyHeld);
        Assert.True(plan.Wall.Reachable, "The objective must be reachable from nothing.");

        // ── Stage 3: prove the frontier concept, for real ────────────────────
        // Unaided APPLY evidence — the kind a model cannot fabricate and the kind that is allowed to
        // cross the hold threshold on its own (the Evidence Ceiling's floor).
        await ProveConcept(learner, _frontier);

        // ── Stage 4: the Twin now holds it — the evidence actually moved belief ─
        var held = await GetConcept(client, _frontier);
        Assert.True(held.IsHeld, "Accepted APPLY evidence should have carried tokenisation to HELD.");
        Assert.True(
            held.Retrievability > 0.90,
            $"Freshly proved, retrievability should be near 1.0, not {held.Retrievability:P0}.");
        Assert.True(held.MasteryCredit > 0, "Holding a concept means it carries mastery credit.");

        // ── Stage 5: the plan recompiles around what is now known ────────────
        // This is the seam that matters: the Compiler reads the Twin, so proving one concept must
        // shrink the road to the objective — not leave the learner re-learning what they hold.
        var recompiled = await GetPlan(client);

        Assert.True(
            recompiled.ConceptsAlreadyHeld >= 1,
            "The recompiled plan should count tokenisation among what is already held.");
        Assert.DoesNotContain(
            recompiled.Steps,
            s => s.Slug == "tokenisation" && s.Kind == "Learn");
        Assert.True(
            recompiled.Steps.Count < plan.Steps.Count,
            $"Proving a concept should leave fewer to learn ({recompiled.Steps.Count} vs {plan.Steps.Count}).");
        Assert.True(recompiled.Believable, "Removing work should not make the plan less believable.");
        Assert.Equal("beating-the-baseline", recompiled.Steps[^1].Slug);
    }

    // ── The script's steps, each a real HTTP call a learner would make ───────

    private async Task<(HttpClient Client, Guid Learner)> Register()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"journey-{Guid.CreateVersion7():N}@example.com",
                displayName = "Journey",
                password = "a good long passphrase",
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth.LearnerId);
    }

    private async Task SetObjective(HttpClient client, int minutesPerDay)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/compiler/objectives", UriKind.Relative),
            new { conceptId = _objective, minutesPerDay, deadline = (DateTime?)null },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlanBody> GetPlan(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<PlanBody>(response)).Data!;
    }

    /// Accepted, unaided APPLY evidence, on the log, now — the fact the diagnostic would have produced.
    private async Task ProveConcept(Guid learner, Guid concept)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var appended = await store.AppendAsync(
            learner,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, Score: 1.0, Assurance: 1.0),
            DateTime.UtcNow,
            concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private static async Task<ConceptBody> GetConcept(HttpClient client, Guid concept)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/twin/concepts/{concept}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<ConceptBody>(response)).Data!;
    }

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;

    private static string SeedPack()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "ai-engineering", "pack.yaml"));
    }

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record PlanBody(
        Guid Objective,
        IReadOnlyList<StepBody> Steps,
        double TotalMinutes,
        int ConceptsAlreadyHeld,
        int ConceptsNeedingRefresh,
        double DaysAtStatedPace,
        bool Believable,
        WallBody Wall);

    private sealed record StepBody(
        Guid ConceptId, string Slug, int Position,
        double Minutes, double CumulativeMinutes, int DayReached, string Kind);

    private sealed record WallBody(
        bool Reachable, int ConceptsReached, int ConceptsInPlan,
        int DaysToObjective, int HorizonDays,
        double PeakMaintenanceMinutesPerDay, double MaintenanceShareOfDay);

    private sealed record ConceptBody(
        double PKnows,
        double PKnowsAtLastEvidence,
        double MasteryCredit,
        bool IsHeld,
        double Retrievability,
        double? DaysUntilDue);
}
