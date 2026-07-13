using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    private async Task<HttpClient> Learner()
    {
        var client = _api.Factory.CreateClient();

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

        return client;
    }

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record OpenLoopBody(Guid LoopId, string Claim, double Confidence);

    private sealed record BoardBody(
        IReadOnlyList<OpenLoopBody> Open,
        IReadOnlyList<OpenLoopBody> ResolvingToday,
        IReadOnlyList<OpenLoopBody> Overdue,
        int Resolved,
        int Forfeited,
        double? Coverage,
        string Headline);
}
