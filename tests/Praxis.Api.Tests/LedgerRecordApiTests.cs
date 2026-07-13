using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **Your own record, through the front door** — proving the Ledger's issuance path is wired into the
/// host: the snapshot reads the (empty) Twin, the private key signs inside the composition root, and a
/// signed record comes back on the very first request. It is *your* record — anonymous callers get 401.
[Collection(SharedApi.Name)]
public sealed class LedgerRecordApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public LedgerRecordApiTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_new_learner_gets_a_signed_record_of_nothing_proved()
    {
        var client = await Learner();

        using var response = await client.GetAsync(new Uri("/api/ledger/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var record = (await Read<RecordBody>(response)).Data!;

        // Honest: nothing proved yet, but a real, signed record — not an error, and not a refusal.
        Assert.Empty(record.Competencies);
        Assert.False(string.IsNullOrEmpty(record.Signature));
        Assert.False(string.IsNullOrEmpty(record.PublicKey));
    }

    [Fact]
    public async Task Only_the_learner_can_mint_their_record()
    {
        using var anonymous = _api.Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/ledger/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> Learner()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"ledger-{Guid.CreateVersion7():N}@example.com",
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

    private sealed record CompetencyBody(Guid ConceptId, string ConceptName, double Assurance);

    private sealed record RecordBody(
        string Subject,
        IReadOnlyList<CompetencyBody> Competencies,
        string Signature,
        string PublicKey,
        DateTime IssuedAt);
}
