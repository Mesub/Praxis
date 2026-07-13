using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The Loops board, through the front door.** Proves the Reality module is actually wired into the
/// host — the controller is discovered, the handler is registered, and the board is computed from a
/// learner's (empty) event stream on the very first request.
[Collection(SharedApi.Name)]
public sealed class RealityLoopsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public RealityLoopsApiTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_new_learner_sees_an_empty_board_that_explains_itself()
    {
        var client = await Learner();

        using var response = await client.GetAsync(new Uri("/api/reality/loops", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var board = (await Read<BoardBody>(response)).Data!;

        Assert.Empty(board.Open);
        Assert.Equal(0, board.Resolved);
        Assert.Null(board.Coverage);
        Assert.Contains("No open predictions", board.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_board_requires_a_learner()
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/reality/loops", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_committed_prediction_lands_on_the_board_and_cannot_be_taken_back()
    {
        var client = await Learner();

        using var committed = await client.PostAsJsonAsync(
            new Uri("/api/reality/loops", UriKind.Relative),
            new
            {
                kind = "output",
                code = "print(2 + 2)",
                answer = "4",
                confidence = 0.8,
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var receipt = (await Read<CommitBody>(committed)).Data!;
        Assert.NotEqual(Guid.Empty, receipt.LoopId);

        // It is now on the board, and there is no endpoint that could remove it.
        using var boardResponse = await client.GetAsync(new Uri("/api/reality/loops", UriKind.Relative));
        var board = (await Read<BoardBody>(boardResponse)).Data!;

        Assert.Single(board.Open);
        Assert.Equal("4", board.Open[0].Claim);
        // A code Loop resolves on demand — its horizon is now, so it is what resolves today.
        Assert.Single(board.ResolvingToday);
    }

    [Fact]
    public async Task A_prediction_without_a_valid_confidence_is_refused()
    {
        var client = await Learner();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/reality/loops", UriKind.Relative),
            new { kind = "output", code = "print(1)", answer = "1", confidence = 1.5 },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resolving_an_unknown_loop_is_not_found()
    {
        var client = await Learner();

        using var response = await client.PostAsync(
            new Uri($"/api/reality/loops/{Guid.CreateVersion7()}/resolve", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_committed_prediction_resolves_against_reality()
    {
        // A sandbox that "runs" the code and prints 4 — deterministic, so the test needs no Docker.
        using var factory = FactoryWithSandbox(
            Result<ExecutionResult>.Success(new ExecutionResult(ExecutionOutcome.Passed, 0, "4\n", "", 5)));
        var client = await LearnerOn(factory);

        var loopId = await Commit(client, code: "print(2 + 2)", answer: "4");

        using var resolved = await client.PostAsync(
            new Uri($"/api/reality/loops/{loopId}/resolve", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var verdict = (await Read<ResolveBody>(resolved)).Data!;
        Assert.True(verdict.Correct);

        // The board now counts it as resolved.
        using var boardResponse = await client.GetAsync(new Uri("/api/reality/loops", UriKind.Relative));
        var board = (await Read<BoardBody>(boardResponse)).Data!;
        Assert.Empty(board.Open);
        Assert.Equal(1, board.Resolved);
    }

    [Fact]
    public async Task A_sandbox_outage_leaves_the_prediction_open_and_marks_nothing()
    {
        using var factory = FactoryWithSandbox(Result<ExecutionResult>.Invalid("Docker is down."));
        var client = await LearnerOn(factory);

        var loopId = await Commit(client, code: "print(2 + 2)", answer: "4");

        using var resolved = await client.PostAsync(
            new Uri($"/api/reality/loops/{loopId}/resolve", UriKind.Relative), null);

        // Not a verdict — the daemon was down, and a wrong resolution would poison calibration.
        Assert.NotEqual(HttpStatusCode.OK, resolved.StatusCode);

        // The loop is still open. Nothing was written against the learner.
        using var boardResponse = await client.GetAsync(new Uri("/api/reality/loops", UriKind.Relative));
        var board = (await Read<BoardBody>(boardResponse)).Data!;
        Assert.Single(board.Open);
        Assert.Equal(0, board.Resolved);
    }

    private WebApplicationFactory<Program> FactoryWithSandbox(Result<ExecutionResult> outcome)
        => _api.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISandbox>();
                services.AddScoped<ISandbox>(_ => new StubSandbox(outcome));
            }));

    private async Task<Guid> Commit(HttpClient client, string code, string answer)
    {
        using var committed = await client.PostAsJsonAsync(
            new Uri("/api/reality/loops", UriKind.Relative),
            new { kind = "output", code, answer, confidence = 0.8 },
            Json);

        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        return (await Read<CommitBody>(committed)).Data!.LoopId;
    }

    private async Task<HttpClient> LearnerOn(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        await Register(client);
        return client;
    }

    private async Task<HttpClient> Learner()
    {
        var client = _api.Factory.CreateClient();
        await Register(client);
        return client;
    }

    private async Task Register(HttpClient client)
    {
        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"loops-{Guid.CreateVersion7():N}@example.com",
                displayName = "Learner",
                password = "a good long passphrase",
            },
            Json);

        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record CommitBody(Guid LoopId, string Kind, string Answer, double Confidence);

    private sealed record OpenLoopBody(Guid LoopId, string Claim, double Confidence);

    private sealed record BoardBody(
        IReadOnlyList<OpenLoopBody> Open,
        IReadOnlyList<OpenLoopBody> ResolvingToday,
        IReadOnlyList<OpenLoopBody> Overdue,
        int Resolved,
        int Forfeited,
        double? Coverage,
        string Headline);

    private sealed record ResolveBody(
        Guid LoopId, bool Correct, double BrierScore, string Predicted, string GroundTruth, double Confidence);

    /// A sandbox that returns a scripted outcome, so the resolve path is deterministic without Docker.
    private sealed class StubSandbox(Result<ExecutionResult> outcome) : ISandbox
    {
        public Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
            => Task.FromResult(outcome);
    }
}
