using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// A fixed Ed25519 seed the server signs with in tests, so a test can issue a record that verifies —
/// and the same seed lets the test build genuine records. In production the seed is a secret.
internal static class LedgerTestKey
{
    // 32 deterministic bytes.
    public static byte[] PrivateSeed { get; } = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    public static string PrivateSeedBase64 => Convert.ToBase64String(PrivateSeed);

    public static byte[] PublicKey => LedgerSigning.PublicKeyFor(PrivateSeed);
}

/// **The public, free-forever verification surface, over real HTTP.**
///
/// The point of the whole Ledger is that a stranger can verify a record. These drive that endpoint the
/// way a stranger would — no account, no token — and check that it trusts the maths and the *server's*
/// key, never anything the record asserts about itself.
[Collection(SharedApi.Name)]
public sealed class LedgerVerificationApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private readonly ApiFixture _api;

    public LedgerVerificationApiTests(ApiFixture api) => _api = api;

    // ── It is public — no account, no token ──────────────────────────────────

    /// **Anyone can fetch the trust root.** No auth: a verifier who has never heard of PRAXIS gets the
    /// public key and can check records forever, offline.
    [Fact]
    public async Task The_public_key_is_served_to_anyone()
    {
        var client = _api.Factory.CreateClient();   // no Authorization header

        using var response = await client.GetAsync(new Uri("/api/ledger/public-key", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<PublicKeyBody>(response);

        Assert.Equal("Ed25519", body.Data!.Algorithm);
        Assert.Equal(Convert.ToBase64String(LedgerTestKey.PublicKey), body.Data.PublicKeyBase64);
    }

    /// **A genuine record verifies — with no login.**
    [Fact]
    public async Task A_genuine_record_verifies_over_http_without_authentication()
    {
        var client = _api.Factory.CreateClient();

        var record = Issue(LedgerTestKey.PrivateSeed, embeddedPublicKey: LedgerTestKey.PublicKey);

        var result = await Verify(client, record);

        Assert.True(result.Valid, result.Reason);
        Assert.Equal(2, result.VerifiedCompetencies);
    }

    // ── The load-bearing refusal: the server's key, never the record's ───────

    /// **A record that lies about its own key is still checked against the server's key.**
    ///
    /// The record carries a *garbage* embedded public key — but it was genuinely signed by the server's
    /// private key, so it verifies. The endpoint never looked at `record.PublicKey`, which is the whole
    /// guarantee: the record does not get to nominate the authority that vouches for it.
    [Fact]
    public async Task Verification_ignores_the_key_embedded_in_the_record()
    {
        var client = _api.Factory.CreateClient();

        var record = Issue(LedgerTestKey.PrivateSeed, embeddedPublicKey: new byte[32]);   // lies about its key

        var result = await Verify(client, record);

        Assert.True(result.Valid, "A genuinely-signed record was rejected because its embedded key was junk.");
    }

    /// **A forger's record — signed with the forger's key, and honestly presenting that key — is
    /// rejected**, because the server checks against its own published key, not the one in the record.
    [Fact]
    public async Task A_record_signed_by_a_forger_is_rejected()
    {
        var client = _api.Factory.CreateClient();

        var (forgerPrivate, forgerPublic) = LedgerSigning.GenerateKeyPair();

        // Internally consistent: signed by the forger, and carrying the forger's matching key.
        var forged = Issue(forgerPrivate, embeddedPublicKey: forgerPublic);

        var result = await Verify(client, forged);

        Assert.False(result.Valid);
        Assert.Contains("not issued by PRAXIS", result.Reason!, StringComparison.Ordinal);
    }

    // ── The human-readable page ──────────────────────────────────────────────

    /// **A person gets a page, not JSON** — with the verdict, and the competencies laid out.
    [Fact]
    public async Task The_verification_page_renders_the_verdict_for_a_human()
    {
        var client = _api.Factory.CreateClient();

        var record = Issue(LedgerTestKey.PrivateSeed, LedgerTestKey.PublicKey);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/ledger/verify/page", UriKind.Relative), record, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Verified — issued by PRAXIS", html, StringComparison.Ordinal);
        Assert.Contains("Rate limiting", html, StringComparison.Ordinal);
    }

    /// **The page escapes the record's untrusted text.** A subject field of `<script>` must not become
    /// a script tag — a verification page XSS'd by the thing it verifies would be its own worst
    /// advertisement.
    [Fact]
    public async Task The_verification_page_escapes_untrusted_fields()
    {
        var client = _api.Factory.CreateClient();

        var record = Issue(LedgerTestKey.PrivateSeed, LedgerTestKey.PublicKey)
            with { Subject = "<script>alert(1)</script>" };

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/ledger/verify/page", UriKind.Relative), record, Json);

        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static async Task<VerificationBody> Verify(HttpClient client, CompetencyRecord record)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/ledger/verify", UriKind.Relative), record, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<VerificationBody>(response)).Data!;
    }

    /// Build a genuine two-competency record signed by `signingSeed`, carrying whatever
    /// `embeddedPublicKey` we want to test the endpoint ignores.
    private static CompetencyRecord Issue(byte[] signingSeed, byte[] embeddedPublicKey)
    {
        var chain = new List<LedgerEntry>();
        var previous = LedgerChain.GenesisHash;

        for (var i = 0; i < 4; i++)
        {
            var e = Evidence.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                EvidenceType.Build, GradingMethod.Execution, 1.0, 1.0, i + 1, When).Value;
            var entry = LedgerChain.Append(e, previous);
            chain.Add(entry);
            previous = entry.Hash;
        }

        var leaves = chain.Select(e => e.Hash).ToList();
        var root = LedgerMerkle.Root(chain);

        var checkpoint = new LedgerCheckpoint(chain[^1].Hash, root, chain.Count, When, []);
        var signature = LedgerSigning.Sign(checkpoint.SignedPayload, signingSeed);

        VerifiedCompetency Competency(int index, string name)
        {
            var e = chain[index].Evidence;

            return new VerifiedCompetency(e.ConceptId, name, e.Type, e.GradingMethod, e.Score,
                e.Assurance, chain[index].Hash, LedgerMerkle.Proof(leaves, index));
        }

        return new CompetencyRecord(
            "A. Learner",
            [Competency(0, "Rate limiting"), Competency(1, "Binary search")],
            checkpoint,
            signature,
            embeddedPublicKey,
            When);
    }

    private static async Task<ApiBody<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiBody<T>>(Json))!;

    private sealed record ApiBody<T>(bool Success, T? Data, string? Message);

    private sealed record PublicKeyBody(string Algorithm, string PublicKeyBase64);

    private sealed record VerificationBody(bool Valid, int VerifiedCompetencies, string? Reason);
}
