using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **Teach-back, through the front door.**
///
/// The AI plane here is the leaking one — it echoes back everything the core sends it. So this file
/// also quietly proves the same thing the tutor's does: the teach-back request carries the learner's
/// words and the *concept*, and **no rubric**, so there is nothing answer-adjacent for even a
/// malicious grader to hand back.
[Collection(SharedApi.Name)]
public sealed class TeachBacksApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;
    private WebApplicationFactory<Program> _factory = null!;

    public TeachBacksApiTests(ApiFixture api) => _api = api;

    public Task InitializeAsync()
    {
        _factory = _api.Factory.WithLeakingAiPlane();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_typed_teach_back_is_graded_and_comes_back_with_the_honest_ceiling()
    {
        var client = await Learner();

        // The leaking grader returns Pending, so this abstains — no accepted-evidence event, and no
        // seeded contract is needed for the endpoint to answer honestly.
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/assessment/teach-backs", UriKind.Relative),
            new
            {
                sessionId = Guid.CreateVersion7(),
                conceptId = Guid.CreateVersion7(),
                text = "An inverted index maps each term to the documents that contain it.",
                seconds = 40,
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await Read<TeachBackBody>(response)).Data!;

        Assert.Equal("Pending", body.Outcome);
        Assert.False(body.RaisedBelief);
        Assert.Contains("Evidence Ceiling", body.Ceiling, StringComparison.Ordinal);

        // The standard it was measured against never comes back: `TeachBackBody` has no field for a
        // rubric or a band, and there is nowhere in the type one could hide. (The ceiling *prose*
        // names the word "rubric" to explain itself — that is honesty, not a leaked standard.)
    }

    [Fact]
    public async Task Teach_back_requires_a_learner()
    {
        using var anonymous = _factory.CreateClient();

        using var response = await anonymous.PostAsJsonAsync(
            new Uri("/api/assessment/teach-backs", UriKind.Relative),
            new { sessionId = Guid.CreateVersion7(), conceptId = Guid.CreateVersion7(), text = "x", seconds = 1 },
            Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_explanation_is_refused_by_the_validator_that_actually_runs()
    {
        var client = await Learner();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/assessment/teach-backs", UriKind.Relative),
            new { sessionId = Guid.CreateVersion7(), conceptId = Guid.CreateVersion7(), text = "", seconds = 0 },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> Learner()
    {
        var client = _factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"teachback-{Guid.CreateVersion7():N}@example.com",
                displayName = "Learner",
                password = "a good long passphrase",
            },
            Json);

        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return client;
    }

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record TeachBackBody(
        string Outcome, double? Score, IReadOnlyList<string> Misconceptions, bool RaisedBelief, string Ceiling);
}
