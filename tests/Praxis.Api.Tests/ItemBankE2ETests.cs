using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The end-to-end proof (M14.7) — an empty bank fills itself, the diagnostic runs, and the answer
/// never crosses the wire.**
///
/// M13.1 deferred the item bank: the adaptive diagnostic read from a table nothing ever filled, and
/// refused honestly rather than invent an item. M14.1–M14.6 wired the whole path that fills it —
/// durable answer keys on the AI plane (M14.1), the generator handed the concept's declared wrong
/// beliefs (M14.2), the .NET side of the wire remembering the oracle's own name for an item
/// (`OracleId`, M14.3), the `IConceptIntel` seam (M14.4), `EnsureConceptPool` (M14.5), and the web
/// diagnostic's best-effort top-up (M14.6). This is the proof that all six pieces actually meet: a
/// learner registers against a concept with an **empty** bank, tops it up, is served a generated
/// item, answers it, and the fact lands on the immutable log — while the answer itself is proven,
/// structurally, never to have appeared in any request this process sent or any response it read.
///
/// The fake AI plane below is modelled on `LeakingAiPlane` (`SessionsApiTests.cs`): it is the
/// adversary and the accountant in one object. It hands back deterministic, never-before-seen items
/// (so the "empty bank fills itself" claim is real, not stubbed away), and it captures **every**
/// request the core ever sent it — so the adversarial sweep at the end has something real to sweep.
[Collection(SharedApi.Name)]
public sealed class ItemBankE2ETests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// **Property names, not free text — but *compound* property names count.** The sweep parses
    /// every captured request and every response body as JSON and checks property names the way
    /// `SchemaTests` checks column names: any name *containing* one of these fragments is
    /// answer-shaped, so `answerKey` and `expectedOutput` are caught, not just bare `answer`.
    ///
    /// The one place a fragment legitimately appears in a key is the `knownMisconceptions` map,
    /// where a declared wrong belief's slug — `models-should-always-answer`, on this very concept —
    /// crosses the wire as a JSON object key. That is data wearing a key's clothes, not a schema
    /// field, and the walker spares that map's keys (and only them).
    private static readonly string[] ForbiddenNameFragments =
    [
        "answer",
        "solution",
        "expected",
        "correct_response",
        "key_text",
        "ground_truth",
        "rubric",
    ];

    private readonly ApiFixture _api;

    /// The journey's concept. Deliberately `grounded-generation` — it declares
    /// `models-should-always-answer`, the adversarial case described above.
    private Guid _conceptForJourney;

    /// A second, untouched concept for the failure test, so the two tests' pools cannot interact.
    private Guid _conceptForFailure;

    public ItemBankE2ETests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        // Idempotent: another class in this collection may already have loaded the real pack.
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var loaded = await loader.LoadAsync(SeedPack());
        Assert.True(
            loaded.IsSuccess || loaded.Status == ResultStatus.Conflict,
            string.Join(Environment.NewLine, loaded.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        _conceptForJourney = graph.Concepts.Single(c => c.Slug == "grounded-generation").Id;
        _conceptForFailure = graph.Concepts.Single(c => c.Slug == "embeddings").Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_empty_bank_fills_itself_the_diagnostic_runs_and_the_answer_never_crosses_the_wire()
    {
        var ai = new ScriptedAiPlane();
        using var factory = BuildFactory(ai);

        // Every response body this test itself reads, alongside every request the core sent the AI
        // plane, is what the closing sweep checks — the adversary gets everything a real jailbreak
        // attempt would see.
        var responseBodies = new List<string>();

        // ── 1. Register a learner; confirm the bank is empty for the concept (no seeding) ────────
        var client = factory.CreateClient();
        var learner = await Register(client, responseBodies);

        var sessionId = Guid.CreateVersion7();

        using (var beforeTopUp = await client.GetAsync(NextItemUri(sessionId, _conceptForJourney)))
        {
            var body = await beforeTopUp.Content.ReadAsStringAsync();
            responseBodies.Add(body);

            // Nothing banked yet: PRAXIS refuses to invent an item rather than serve one.
            Assert.Equal(HttpStatusCode.NotFound, beforeTopUp.StatusCode);
            Assert.Contains("will not invent one", body, StringComparison.Ordinal);
        }

        // ── 2. POST /api/assessment/diagnostics/pool → 200, generated == 5 ───────────────────────
        PoolBody pool;
        using (var response = await client.PostAsJsonAsync(
            PoolUri(), new { conceptId = _conceptForJourney }, Json))
        {
            var body = await response.Content.ReadAsStringAsync();
            responseBodies.Add(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            pool = (await ReadFrom<PoolBody>(body)).Data!;
        }

        Assert.Equal(5, pool.Generated);
        Assert.Equal(5, pool.PoolSize);
        Assert.Equal(5, ai.GenerateCalls.Count);

        // ── 3. GET next-item → 200 with one of the generated stems ───────────────────────────────
        NextItemBody item;
        using (var response = await client.GetAsync(NextItemUri(sessionId, _conceptForJourney)))
        {
            var body = await response.Content.ReadAsStringAsync();
            responseBodies.Add(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            item = (await ReadFrom<NextItemBody>(body)).Data!;
        }

        Assert.False(item.Stopped);
        Assert.NotNull(item.ItemId);
        Assert.NotNull(item.Stem);
        Assert.Contains(
            ai.GeneratedStems, stem => string.Equals(stem, item.Stem, StringComparison.Ordinal));

        // ── 4. POST answers → 200, outcome "Correct" ──────────────────────────────────────────────
        AnswerBody answer;
        using (var response = await client.PostAsJsonAsync(
            AnswersUri(sessionId),
            new
            {
                itemId = Guid.Parse(item.ItemId!),
                claim = "The system should say it does not know when the passages do not say so.",
                statedConfidence = 0.8,
                seconds = 25,
            },
            Json))
        {
            var body = await response.Content.ReadAsStringAsync();
            responseBodies.Add(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            answer = (await ReadFrom<AnswerBody>(body)).Data!;
        }

        Assert.Equal("Correct", answer.Outcome);

        // ── 5. The oracle was asked under the generated item's oracle id, not our Guid ───────────
        var checkedClaim = Assert.Single(ai.CheckClaimCalls);
        Assert.NotEqual(item.ItemId, checkedClaim.ItemId);
        Assert.StartsWith("item-e2e-", checkedClaim.ItemId, StringComparison.Ordinal);

        // ── 6. The immutable log now holds an AnswerGiven for this learner and concept ───────────
        await using (var scope = _api.Factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var stream = await store.ReadStreamAsync(learner);

            var answerEvent = Assert.Single(stream, e => e.Type == LearningEventType.AnswerGiven);
            Assert.Equal(_conceptForJourney, answerEvent.ConceptId);

            var payload = EventCatalogue.Deserialize<AnswerGiven>(answerEvent);
            Assert.Equal(Guid.Parse(item.ItemId!), payload.ItemId);
            Assert.True(payload.Correct);
        }

        // ── 7. The adversarial sweep ───────────────────────────────────────────────────────────────
        // Every request the core sent the AI plane, serialized exactly as it was sent, plus every
        // response body this test read off the wire. None of it carries an answer-shaped field.
        foreach (var request in ai.GenerateCalls)
        {
            AssertNoAnswerShapedField(JsonSerializer.Serialize(request, Json));
        }

        foreach (var request in ai.CheckClaimCalls)
        {
            AssertNoAnswerShapedField(JsonSerializer.Serialize(request, Json));
        }

        foreach (var body in responseBodies)
        {
            AssertNoAnswerShapedField(body);
        }
    }

    /// **The other half of the refusal.** A pool that cannot generate is not the same failure as an
    /// empty pool that was never asked to fill — but the outcome the learner sees must be identical:
    /// an honest refusal, never an invented item.
    [Fact]
    public async Task Pool_generation_failure_banks_nothing_and_the_diagnostic_refuses_honestly()
    {
        var ai = new ScriptedAiPlane(generationFails: true);
        using var factory = BuildFactory(ai);

        var client = factory.CreateClient();
        await Register(client, responseBodies: []);

        var sessionId = Guid.CreateVersion7();

        // The pool call carries the failure — it does not silently report success with nothing
        // generated, and it does not swallow the error into a 200.
        using (var response = await client.PostAsJsonAsync(
            PoolUri(), new { conceptId = _conceptForFailure }, Json))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.NotEmpty(ai.GenerateCalls);

        // next-item still refuses. No item was banked, so there is nothing honest to serve — and
        // PRAXIS does not invent one just because the pool call failed on its way here.
        using (var response = await client.GetAsync(NextItemUri(sessionId, _conceptForFailure)))
        {
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("will not invent one", body, StringComparison.Ordinal);
        }
    }

    /// The sweep, swept. `SchemaTests` proves no *column* can be answer-shaped; this proves the
    /// wire sweep above holds the same standard — compound names are caught, and the one lawful
    /// appearance of a fragment in a key (a declared wrong-belief slug in the misconceptions map)
    /// does not scare it into a false positive.
    [Fact]
    public void The_sweep_catches_compound_answer_shaped_names_and_spares_misconception_slugs()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertNoAnswerShapedField("""{"answerKey": "leak"}"""));

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertNoAnswerShapedField("""{"data": {"expectedOutput": "leak"}}"""));

        AssertNoAnswerShapedField(
            """{"knownMisconceptions": {"models-should-always-answer": "a stated wrong belief"}}""");
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> BuildFactory(IAiPlane aiPlane)
        => _api.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiPlane>();

                // The same instance backs every request in the test — a fresh instance per DI scope
                // (the ordinary `AddScoped<IAiPlane, T>` shape) would lose everything it captured the
                // moment the HTTP request that captured it ended.
                services.AddScoped<IAiPlane>(_ => aiPlane);
            }));

    private static Uri PoolUri() => new("/api/assessment/diagnostics/pool", UriKind.Relative);

    private static Uri NextItemUri(Guid sessionId, Guid conceptId) => new(
        $"/api/assessment/diagnostics/{sessionId}/next-item?conceptId={conceptId}", UriKind.Relative);

    private static Uri AnswersUri(Guid sessionId) => new(
        $"/api/assessment/diagnostics/{sessionId}/answers", UriKind.Relative);

    private static async Task<Guid> Register(HttpClient client, List<string> responseBodies)
    {
        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"item-bank-{Guid.CreateVersion7():N}@example.com",
                displayName = "ItemBank",
                password = "a good long passphrase",
            },
            Json);

        var body = await registered.Content.ReadAsStringAsync();
        responseBodies.Add(body);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var auth = (await ReadFrom<AuthBody>(body)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return auth.LearnerId;
    }

    private static Task<ApiResponse<T>> ReadFrom<T>(string body)
        => Task.FromResult(JsonSerializer.Deserialize<ApiResponse<T>>(body, Json)!);

    private static void AssertNoAnswerShapedField(string json)
    {
        using var doc = JsonDocument.Parse(json);
        WalkForForbiddenPropertyNames(doc.RootElement, keysAreData: false);
    }

    private static void WalkForForbiddenPropertyNames(JsonElement element, bool keysAreData)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.False(
                        !keysAreData && ForbiddenNameFragments.Any(f =>
                            property.Name.Contains(f, StringComparison.OrdinalIgnoreCase)),
                        $"'{property.Name}' is an answer-shaped property name, and nothing PRAXIS "
                        + "sends or receives should ever carry one.");

                    // A misconceptions map's keys are declared wrong-belief slugs — data wearing a
                    // key's clothes — and only they are spared the name check.
                    WalkForForbiddenPropertyNames(
                        property.Value,
                        keysAreData: property.Name.Equals(
                            "knownMisconceptions", StringComparison.OrdinalIgnoreCase));
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkForForbiddenPropertyNames(item, keysAreData: false);
                }

                break;
        }
    }

    private static string SeedPack()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "ai-engineering", "pack.yaml"));
    }

    // ── Bodies ─────────────────────────────────────────────────────────────────

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record PoolBody(int Generated, int PoolSize);

    private sealed record NextItemBody(
        string? ItemId,
        string? Stem,
        string? EvidenceType,
        double Theta,
        double StandardError,
        bool ConfidentEnoughToStop,
        bool Stopped,
        string? Says);

    private sealed record AnswerBody(
        string Outcome, string? Misconception, bool YouSaidYouWereSure, string AndYouWere);
}

/// **The adversary and the accountant, in one object.** Modelled on `LeakingAiPlane`
/// (`SessionsApiTests.cs`): every request the core sends is captured verbatim, so the closing sweep
/// has the real wire traffic to check rather than a stand-in for it.
///
/// `GenerateItemAsync` hands back deterministic, never-before-seen items — unique stems and
/// fingerprints, `targets_misconception: null` — so "the bank fills itself" is a fact this test
/// observes, not a fixture it assumes. `CheckClaimAsync` always says `Correct`, and records the exact
/// request it was asked to check.
internal sealed class ScriptedAiPlane : IAiPlane
{
    private readonly bool _generationFails;
    private int _generated;

    public List<GenerateItemRequest> GenerateCalls { get; } = [];

    public List<CheckClaimRequest> CheckClaimCalls { get; } = [];

    public List<string> GeneratedStems { get; } = [];

    public ScriptedAiPlane(bool generationFails = false) => _generationFails = generationFails;

    public Task<Result<ItemResponse>> GenerateItemAsync(
        GenerateItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        GenerateCalls.Add(request);

        if (_generationFails)
        {
            return Task.FromResult(Result<ItemResponse>.Invalid(
                "The generator could not be reached."));
        }

        _generated++;
        var n = _generated;
        var stem = $"Generated item {n}: a stem nobody in this process has read the answer to.";

        GeneratedStems.Add(stem);

        return Task.FromResult(Result<ItemResponse>.Success(new ItemResponse(
            Id: $"item-e2e-{n}",
            ConceptId: request.ConceptId,
            EvidenceType: request.EvidenceType,
            Stem: stem,
            Difficulty: request.Difficulty,
            Fingerprint: $"fp-e2e-{n}",
            TargetsMisconception: null)));
    }

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CheckClaimCalls.Add(request);

        return Task.FromResult(Result<CheckClaimResponse>.Success(
            new CheckClaimResponse(ClaimOutcome.Correct, null, 0.001)));
    }

    public Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not exercised by this journey.");

    public Task<Result<TutorTurnResponse>> TutorTurnAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not exercised by this journey.");

    public IAsyncEnumerable<string> TutorTurnStreamAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not exercised by this journey.");
}
