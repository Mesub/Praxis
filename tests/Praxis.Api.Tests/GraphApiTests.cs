using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// The Graph's endpoints, through the real host: authentication, the seed pack, and the frontier.
[Collection(SharedApi.Name)]
public sealed class GraphApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public GraphApiTests(ApiFixture api) => _api = api;

    /// Load the real pack once, through the app's own loader.
    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();

        var result = await loader.LoadAsync(SeedPack());

        // Conflict means a previous test class already loaded it — fine.
        Assert.True(
            result.IsSuccess || result.Status == ResultStatus.Conflict,
            string.Join("\n", result.Errors));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_graph_is_not_public()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/graph/concepts?domain=ai-engineering", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_learner_can_list_the_domain()
    {
        using var client = await AuthenticatedClient();

        using var response = await client.GetAsync(
            new Uri("/api/graph/concepts?domain=ai-engineering&pageSize=200", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<PagedResult<ConceptBody>>(response);

        Assert.True(body.Success);
        Assert.Equal(30, body.Data!.TotalCount);
    }

    [Fact]
    public async Task A_concept_shows_the_standard_it_will_be_held_to()
    {
        using var client = await AuthenticatedClient();

        using var response = await client.GetAsync(
            new Uri("/api/graph/concepts/beating-the-baseline", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<ConceptDetailBody>(response);
        var proof = body.Data!.Proof;

        Assert.Equal(0.90, proof.HoldThreshold);

        // The learner is told, up front, the most an AI's opinion of them could ever be worth —
        // and that it is not enough.
        Assert.True(proof.ModelGradedCeiling < proof.HoldThreshold);

        // And that this one has to actually run.
        Assert.Contains(proof.Accepted, a => a.Type == EvidenceType.Build && !a.ModelGraded);
    }

    [Fact]
    public async Task An_unknown_concept_is_a_404_not_a_500()
    {
        using var client = await AuthenticatedClient();

        using var response = await client.GetAsync(
            new Uri("/api/graph/concepts/does-not-exist", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_frontier_of_a_new_learner_is_one_concept_and_the_rest_have_distances()
    {
        using var client = await AuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/graph/concepts/frontier", UriKind.Relative),
            new { domain = "ai-engineering", held = Array.Empty<Guid>() },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<FrontierBody>(response);

        Assert.Single(body.Data!.Ready);
        Assert.Equal("tokenisation", body.Data.Ready[0].Slug);

        // Everything else is a distance, not a locked door.
        Assert.NotEmpty(body.Data.Blocked);
        Assert.All(body.Data.Blocked, b => Assert.True(b.MissingPrerequisites > 0));
    }

    private async Task<HttpClient> AuthenticatedClient()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"graph-{Guid.CreateVersion7():N}@example.com",
                displayName = "Graph",
                password = "a good long passphrase",
            },
            Json);

        var auth = await Read<AuthBody>(registered);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Data!.AccessToken);

        return client;
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
    private sealed record ConceptBody(Guid Id, string Slug, string Title, double DifficultyPrior);
    private sealed record ConceptDetailBody(ConceptBody Concept, string[] Requires, string[] Contrasts, ProofBody Proof);
    private sealed record ProofBody(double HoldThreshold, EvidenceClauseBody[] Accepted, double ModelGradedCeiling);
    private sealed record EvidenceClauseBody(EvidenceType Type, double MinScore, double MaxContribution, int RequiredCount, bool ModelGraded);
    private sealed record FrontierBody(ConceptBody[] Ready, BlockedBody[] Blocked);
    private sealed record BlockedBody(Guid ConceptId, string Slug, int MissingPrerequisites);
}
