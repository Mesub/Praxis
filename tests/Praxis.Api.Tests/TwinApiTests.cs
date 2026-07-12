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

/// **The Twin, through the front door.**
///
/// A learner registers, produces evidence, and looks at themselves. The Evidence Ceiling is proved
/// again here — not as a unit, not in the projection, but end to end through real HTTP, because
/// that is the only place the claim actually reaches a human.
[Collection(SharedApi.Name)]
public sealed class TwinApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private Guid _concept;

    public TwinApiTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var result = await loader.LoadAsync(SeedPack());

        Assert.True(result.IsSuccess || result.Status == ResultStatus.Conflict, string.Join("\n", result.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        // A concept whose contract demands a BUILD — it ran, or it did not.
        _concept = graph.Concepts.Single(c => c.Slug == "hnsw").Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_twin_is_not_public()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/twin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_new_learner_knows_nothing_and_the_twin_says_so_honestly()
    {
        var (client, _) = await Learner();

        using var response = await client.GetAsync(new Uri("/api/twin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var twin = (await Read<TwinBody>(response)).Data!;

        Assert.Equal(0, twin.ConceptsHeld);
        Assert.Equal(0, twin.ActiveMisconceptions);

        // And it does not claim to have measured their calibration on zero items.
        Assert.False(twin.CalibrationIsMeaningful);
    }

    [Fact]
    public async Task An_untouched_concept_is_unknown_not_a_404()
    {
        // "We have no evidence about you and this" is a different claim from "this does not exist",
        // and the honest one on day zero.
        var (client, _) = await Learner();

        using var response = await client.GetAsync(
            new Uri($"/api/twin/concepts/{_concept}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = (await Read<StateBody>(response)).Data!;

        Assert.False(state.IsHeld);
        Assert.Equal(0, state.EvidenceCount);
        Assert.Equal(0.0, state.MasteryCredit);

        // And there is no due date, because nothing was ever proved. This is a regression test:
        // the first version returned infinity here, which System.Text.Json cannot serialise — so a
        // learner's very first look at an untouched concept was a 500.
        Assert.Null(state.DaysUntilDue);
        Assert.Null(state.NextDueAt);
    }

    /// **The Evidence Ceiling, at the front door.**
    ///
    /// Fifty perfect explanations, appended as real events, projected through the real Twin, read
    /// back over real HTTP. The learner is *not* held — and the API tells them, plainly, that none
    /// of what they have built rests on work a model could not have faked.
    [Fact]
    public async Task Fifty_perfect_explanations_do_not_produce_mastery_and_the_api_says_why()
    {
        var (client, learner) = await Learner();

        await AppendAsync(learner, Enumerable.Range(0, 50)
            .Select(_ => new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Explain, 1.0, 1.0))
            .ToList());

        var state = await ConceptState(client);

        Assert.False(
            state.IsHeld,
            $"Fifty perfect explanations produced a HELD concept (credit {state.MasteryCredit:F3}). "
            + "A language model has just manufactured mastery, and every claim in the Ledger is "
            + "worthless.");

        Assert.True(state.MasteryCredit > 0, "Talking is worth something. It just never finishes.");

        // The honesty is the product: none of this rests on work a model could not fake.
        Assert.Equal(0.0, state.Assurance, tolerance: 1e-6);
    }

    [Fact]
    public async Task One_build_that_ran_finishes_it()
    {
        var (client, learner) = await Learner();

        await AppendAsync(learner,
            [new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, Score: 1.0, Assurance: 1.0)]);

        var state = await ConceptState(client);

        Assert.True(state.IsHeld, $"A build that ran left them at {state.MasteryCredit:F2}.");
        Assert.Equal(1.0, state.Assurance, tolerance: 1e-6);

        // And it is now something that can rot.
        Assert.NotNull(state.NextDueAt);
    }

    [Fact]
    public async Task The_summary_counts_what_is_held()
    {
        var (client, learner) = await Learner();

        await AppendAsync(learner,
            [new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0)]);

        using var response = await client.GetAsync(new Uri("/api/twin", UriKind.Relative));

        var twin = (await Read<TwinBody>(response)).Data!;

        Assert.Equal(1, twin.ConceptsHeld);
    }

    [Fact]
    public async Task A_learner_only_ever_sees_their_own_twin()
    {
        // There is no ?learnerId= parameter, and there never will be. The most intimate model of a
        // person ever built does not get an admin override.
        var (alice, aliceId) = await Learner();
        var (bob, _) = await Learner();

        await AppendAsync(aliceId,
            [new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0)]);

        Assert.True((await ConceptState(alice)).IsHeld);

        // Bob's Twin knows nothing of Alice's work.
        Assert.False((await ConceptState(bob)).IsHeld);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private async Task AppendAsync(Guid learner, List<EvidenceAccepted> evidence)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var at = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

        var batch = new EventBatch(learner);

        for (var i = 0; i < evidence.Count; i++)
        {
            batch.Add(evidence[i], at.AddMinutes(i), _concept);
        }

        var appended = await store.AppendAsync(learner, batch.Build(), expectedLastSequence: null);
        Assert.True(appended.IsSuccess, appended.Message);

        // Drive the Twin the way the app does.
        var runner = scope.ServiceProvider.GetRequiredService<ProjectionRunner>();
        await runner.CatchUpAsync();
    }

    private async Task<StateBody> ConceptState(HttpClient client)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/twin/concepts/{_concept}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<StateBody>(response)).Data!;
    }

    private async Task<(HttpClient Client, Guid LearnerId)> Learner()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"twin-{Guid.CreateVersion7():N}@example.com",
                displayName = "Twin",
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

    private sealed record StateBody(
        Guid ConceptId, double PKnows, double MasteryCredit, bool IsHeld,
        double Retrievability, double? DaysUntilDue, DateTime? LastEvidenceAt, DateTime? NextDueAt,
        double ConfidenceGap, double BrierScore, bool CalibrationIsMeaningful,
        double Assurance, int EvidenceCount);

    private sealed record TwinBody(
        int ConceptsHeld, int ConceptsInProgress, int ConceptsDecaying, int ConceptsLapsed,
        double OverallBrierScore, double OverallConfidenceGap, bool CalibrationIsMeaningful,
        int ActiveMisconceptions);
}
