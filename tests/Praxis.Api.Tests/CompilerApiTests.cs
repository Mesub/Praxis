using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The Compiler, through the front door — including the refusal.**
///
/// Everything else in this suite proves PRAXIS can compile a plan. These tests prove it can decline
/// to, to a real person, over real HTTP, on the real seed pack. That is the harder half, and it is
/// the half every other product in this industry has quietly declined to build.
[Collection(SharedApi.Name)]
public sealed class CompilerApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private Guid _objective;

    public CompilerApiTests(ApiFixture api) => _api = api;

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
    public async Task A_plan_is_not_public()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── There is no plan without an objective ────────────────────────────────

    /// **404, not 400.** "You have not told me what you want to become" is not a malformed request —
    /// it is a missing thing, and the status code is the difference between a product that explains
    /// itself and one that blames the caller.
    [Fact]
    public async Task A_learner_with_no_objective_gets_a_404_and_is_told_why()
    {
        var (client, _) = await Learner();

        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await Read<PlanBody>(response);

        Assert.Contains("selling motion", body.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_objective_with_no_minutes_behind_it_is_refused()
    {
        var (client, _) = await Learner();

        using var response = await PostObjective(client, minutesPerDay: 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changing_your_mind_retires_the_old_objective_rather_than_stacking_a_second_one()
    {
        // A learner pursuing three things is pursuing none of them, and the Scheduler would have to
        // pick between them anyway — badly, and without saying so.
        var (client, _) = await Learner();

        Assert.Equal(0, (await SetObjectiveAsync(client, 60)).Replaced);
        Assert.Equal(1, (await SetObjectiveAsync(client, 90)).Replaced);

        // And the plan compiles against the new pace, not the old one.
        var plan = await PlanAsync(client);
        Assert.True(plan.Believable);
    }

    // ── The plan ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_serious_pace_gets_the_whole_path_and_PRAXIS_believes_in_it()
    {
        var (client, _) = await Learner();

        await SetObjectiveAsync(client, minutesPerDay: 90);

        var plan = await PlanAsync(client);

        Assert.Equal(30, plan.Steps.Count);
        Assert.Equal("tokenisation", plan.Steps[0].Slug);
        Assert.Equal("beating-the-baseline", plan.Steps[^1].Slug);

        Assert.True(plan.Believable);
        Assert.Null(plan.Alternative);
        Assert.True(plan.Wall.Reachable);

        // Every step is something to *do*, and it says which kind. Nothing is a "module".
        Assert.All(plan.Steps, s => Assert.Equal("Learn", s.Kind));
    }

    // ── The refusal. This is the one. ────────────────────────────────────────

    /// **The Decay Wall, at the front door.**
    ///
    /// A real learner asks PRAXIS to take them from zero to beating BM25 in ninety days, at ten
    /// minutes a day. Every other product in this industry takes that money and lets them discover
    /// the truth in month seven.
    ///
    /// PRAXIS runs the forward simulation *before* it answers, comes back and says **no** — and then
    /// does the thing that makes the no worth having: it offers a narrower objective it actually
    /// believes in, and a number instead of a platitude.
    ///
    /// Note the status: **200, not 400.** The refusal is not an error. It is the product's most
    /// considered opinion, and the plan comes back attached to it, because the plan is theirs.
    [Fact]
    public async Task At_ten_minutes_a_day_PRAXIS_refuses_the_objective_and_offers_something_honest()
    {
        var (client, _) = await Learner();

        await SetObjectiveAsync(client, minutesPerDay: 10, deadline: DateTime.UtcNow.AddDays(90));

        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var plan = (await Read<PlanBody>(response)).Data!;

        Assert.False(
            plan.Believable,
            "PRAXIS agreed to get someone from zero to beating BM25 on ten minutes a day inside "
            + "ninety days. It has just sold a plan it has no reason to believe in, which is the one "
            + "thing this product exists not to do.");

        Assert.False(plan.Wall.Reachable);

        // The plan is still returned. They are entitled to see it — what is withheld is the pretence.
        Assert.NotEmpty(plan.Steps);

        // And the alternative is real: fewer concepts than the objective, and a number they can act
        // on rather than "try to find more time".
        Assert.NotNull(plan.Alternative);
        Assert.NotEmpty(plan.Alternative.Steps);
        Assert.True(plan.Alternative.ConceptsReachable < plan.Alternative.ConceptsInObjective);

        Assert.NotNull(plan.Alternative.MinutesPerDayThatWouldWork);
        Assert.True(plan.Alternative.MinutesPerDayThatWouldWork > 10);
    }

    /// The number nobody else prints: how much of their day goes on **keeping what they already
    /// have**. At a thin pace it approaches all of it — they are running to stand still, and until
    /// PRAXIS simulated it they would have blamed themselves.
    [Fact]
    public async Task The_refusal_shows_its_working()
    {
        var (client, _) = await Learner();

        await SetObjectiveAsync(client, 10, DateTime.UtcNow.AddDays(90));

        var plan = await PlanAsync(client);

        Assert.True(plan.Wall.ConceptsReached < plan.Wall.ConceptsInPlan);
        Assert.Equal(90, plan.Wall.HorizonDays);
        Assert.True(plan.Wall.PeakMaintenanceMinutesPerDay > 0);
        Assert.InRange(plan.Wall.MaintenanceShareOfDay, 0.0, 1.0);
    }

    /// A deadline already gone is not a hard plan — it is an impossible one, and the honest answer
    /// is not an empty roadmap with a sad face on it.
    [Fact]
    public async Task A_deadline_in_the_past_is_a_400_not_an_empty_plan()
    {
        var (client, _) = await Learner();

        await SetObjectiveAsync(client, 60, DateTime.UtcNow.AddDays(-30));

        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await Read<PlanBody>(response);

        Assert.Contains("into the past", body.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_learner_only_ever_sees_their_own_plan()
    {
        // There is no ?learnerId=. The objective is read off the token, and only off the token.
        var (alice, _) = await Learner();
        var (bob, _) = await Learner();

        await SetObjectiveAsync(alice, 90);

        Assert.Equal(30, (await PlanAsync(alice)).Steps.Count);

        using var bobs = await GetPlanAsync(bob);
        Assert.Equal(HttpStatusCode.NotFound, bobs.StatusCode);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> GetPlanAsync(HttpClient client)
        => client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

    private Task<HttpResponseMessage> PostObjective(
        HttpClient client, int minutesPerDay, DateTime? deadline = null)
        => client.PostAsJsonAsync(
            new Uri("/api/compiler/objectives", UriKind.Relative),
            new { conceptId = _objective, minutesPerDay, deadline },
            Json);

    private async Task<ObjectiveBody> SetObjectiveAsync(
        HttpClient client, int minutesPerDay, DateTime? deadline = null)
    {
        using var response = await PostObjective(client, minutesPerDay, deadline);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<ObjectiveBody>(response)).Data!;
    }

    private static async Task<PlanBody> PlanAsync(HttpClient client)
    {
        using var response = await GetPlanAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<PlanBody>(response)).Data!;
    }

    private async Task<(HttpClient Client, Guid LearnerId)> Learner()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"compiler-{Guid.CreateVersion7():N}@example.com",
                displayName = "Compiler",
                password = "a good long passphrase",
            },
            Json);

        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth.LearnerId);
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

    private sealed record AuthBody(Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record ObjectiveBody(Guid Id, Guid ConceptId, int MinutesPerDay, DateTime? Deadline, int Replaced);

    private sealed record PlanBody(
        Guid Objective,
        IReadOnlyList<StepBody> Steps,
        double TotalMinutes,
        int ConceptsAlreadyHeld,
        int ConceptsNeedingRefresh,
        double DaysAtStatedPace,
        bool Believable,
        WallBody Wall,
        AlternativeBody? Alternative);

    private sealed record StepBody(
        Guid ConceptId, string Slug, int Position,
        double Minutes, double CumulativeMinutes, int DayReached, string Kind);

    private sealed record WallBody(
        bool Reachable, int ConceptsReached, int ConceptsInPlan,
        int DaysToObjective, int HorizonDays,
        double PeakMaintenanceMinutesPerDay, double MaintenanceShareOfDay);

    private sealed record AlternativeBody(
        IReadOnlyList<StepBody> Steps, int? MinutesPerDayThatWouldWork,
        int ConceptsReachable, int ConceptsInObjective);
}
