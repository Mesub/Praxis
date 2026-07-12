using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// The auth pipeline, driven through the real host: a learner registers, gets a token, and the
/// system reads *them* off it. Without this, every "the learner" in every later engine is a lie.
[Collection(SharedApi.Name)]
public sealed class AuthPipelineTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public AuthPipelineTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task An_unauthenticated_request_to_a_protected_endpoint_is_401()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/identity/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_token_is_401_not_500()
    {
        using var client = _api.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        using var response = await client.GetAsync(new Uri("/api/identity/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_then_call_a_protected_endpoint_as_that_learner()
    {
        using var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new { email = "e2e@example.com", displayName = "E2E", password = "a good long passphrase" },
            Json);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var auth = await Read<AuthBody>(registered);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Data!.AccessToken);

        using var me = await client.GetAsync(new Uri("/api/identity/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body = await Read<MeBody>(me);

        // The host read the learner off the JWT — not off anything the caller asked it to trust.
        Assert.Equal(auth.Data.LearnerId, body.Data!.LearnerId);
    }

    [Fact]
    public async Task Login_after_registering_yields_a_working_token()
    {
        using var client = _api.Factory.CreateClient();

        using var _ = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new { email = "relogin@example.com", displayName = "Re", password = "a good long passphrase" },
            Json);

        using var loggedIn = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/login", UriKind.Relative),
            new { email = "relogin@example.com", password = "a good long passphrase" },
            Json);

        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);

        var auth = await Read<AuthBody>(loggedIn);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.Data!.AccessToken);

        using var me = await client.GetAsync(new Uri("/api/identity/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_short_password_is_rejected_by_validation_before_the_handler()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new { email = "short@example.com", displayName = "Short", password = "abc" },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await Read<object>(response);
        Assert.Contains(body.Errors!, e => e.Contains("12 characters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wrong_credentials_are_401_and_say_nothing_useful()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/login", UriKind.Relative),
            new { email = "ghost@example.com", password = "a good long passphrase" },
            Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await Read<object>(response);
        Assert.Equal("Email or password is incorrect.", body.Message);
    }

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;

    private sealed record AuthBody(Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);
    private sealed record MeBody(Guid LearnerId);
}
