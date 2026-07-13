using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **Mastery-per-Minute, through the front door** — proving the Analytics module is wired into the
/// host: minutes fold from the (empty) log, mastery from the (empty) Twin, and the honest answer for a
/// learner who has invested nothing comes back on the first request: *no rate yet*, not a fake zero.
[Collection(SharedApi.Name)]
public sealed class AnalyticsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public AnalyticsApiTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_new_learner_has_no_rate_yet_and_it_is_not_faked_as_zero()
    {
        var client = await Learner();

        using var response = await client.GetAsync(new Uri("/api/analytics/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mpm = (await Read<MpmBody>(response)).Data!;

        Assert.Null(mpm.Rate);
        Assert.False(mpm.HasData);
        Assert.Equal(0.0, mpm.MinutesInvested);
    }

    [Fact]
    public async Task The_number_is_yours_alone()
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/analytics/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> Learner()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"analytics-{Guid.CreateVersion7():N}@example.com",
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

    private sealed record MpmBody(
        double? Rate, double MasteryCredit, double MinutesInvested, bool HasData, bool GainedNothing);
}
