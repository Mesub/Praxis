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
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The Evidence Ceiling (C-4), proven end to end — through the front door, on a real contract.**
///
/// This is the refusal that makes the whole product honest: *a language model grading an explanation
/// can raise belief, but it can never — however perfect the score — carry a learner to HELD.* Only
/// applying, building, or predicting against reality can do that. It is not a prompt asking a model to
/// be strict; it is arithmetic in the contract, and this test drives it through a real HTTP teach-back
/// with a grader that says **Passed** as loudly as it can.
///
/// `the-inverted-index` accepts `EXPLAIN` up to 0.20 and holds at 0.75. So a flawless explanation —
/// graded 0.95 by a model — should visibly *move* the Twin's belief, and just as visibly fail to make
/// the concept held. If it ever did, the ceiling would be a comment, not a guarantee.
[Collection(SharedApi.Name)]
public sealed class EvidenceCeilingE2ETests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private WebApplicationFactory<Program> _factory = null!;
    private Guid _explainConcept;   // the-inverted-index: EXPLAIN ≤ 0.20, hold 0.75

    public EvidenceCeilingE2ETests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        // A host whose grader always passes, and passes hard — the adversary for a ceiling is a
        // generous model, so the test hands it the most generous one there is.
        _factory = _api.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiPlane>();
                services.AddScoped<IAiPlane, AlwaysPassesGrader>();
            }));

        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var loaded = await loader.LoadAsync(SeedPack());
        Assert.True(
            loaded.IsSuccess || loaded.Status == ResultStatus.Conflict,
            string.Join(Environment.NewLine, loaded.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        _explainConcept = graph.Concepts.Single(c => c.Slug == "the-inverted-index").Id;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_perfectly_graded_explanation_raises_belief_but_cannot_reach_HELD()
    {
        var client = await Register();

        // Before: unseen. The BKT prior, nothing held, nothing proved.
        var before = await GetConcept(client, _explainConcept);
        Assert.False(before.IsHeld);

        // A flawless teach-back. The grader passes it at 0.95 — the most a model's opinion can say.
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/assessment/teach-backs", UriKind.Relative),
            new
            {
                sessionId = Guid.CreateVersion7(),
                conceptId = _explainConcept,
                text =
                    "An inverted index maps each term to the list of documents that contain it — the "
                    + "postings list — so a query looks up terms directly instead of scanning every "
                    + "document. It is what makes lexical search fast on rare words and exact names.",
                seconds = 180,
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var graded = (await Read<TeachBackBody>(response)).Data!;
        Assert.Equal("Passed", graded.Outcome);
        Assert.True(graded.RaisedBelief, "A passed teach-back is accepted evidence and raises belief.");

        // After: belief has moved — a good explanation is real evidence, and PRAXIS records it.
        var after = await GetConcept(client, _explainConcept);
        Assert.True(
            after.PKnowsAtLastEvidence > before.PKnowsAtLastEvidence,
            $"A passed explanation should raise belief; {before.PKnowsAtLastEvidence:F3} → {after.PKnowsAtLastEvidence:F3}.");

        // ── The ceiling. This is the whole point. ────────────────────────────
        // However good the explanation, EXPLAIN tops out at 0.20 and the concept holds at 0.75. A
        // model cannot talk a learner into mastery — only unfakeable work reaches the threshold.
        Assert.False(
            after.IsHeld,
            "A model-graded explanation carried the concept to HELD — the Evidence Ceiling has failed.");
        Assert.True(
            after.MasteryCredit <= 0.20 + 1e-9,
            $"EXPLAIN mastery credit is capped at 0.20; got {after.MasteryCredit:F3}.");
        Assert.True(
            after.MasteryCredit < 0.75,
            "Mastery credit crossed the hold threshold on talk alone.");
    }

    private async Task<HttpClient> Register()
    {
        var client = _factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"ceiling-{Guid.CreateVersion7():N}@example.com",
                displayName = "Ceiling",
                password = "a good long passphrase",
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return client;
    }

    private async Task<ConceptBody> GetConcept(HttpClient client, Guid concept)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/twin/concepts/{concept}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<ConceptBody>(response)).Data!;
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

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record TeachBackBody(
        string Outcome, double? Score, IReadOnlyList<string> Misconceptions, bool RaisedBelief, string Ceiling);

    private sealed record ConceptBody(
        double PKnows,
        double PKnowsAtLastEvidence,
        double MasteryCredit,
        bool IsHeld,
        double Retrievability,
        double? DaysUntilDue);
}

/// A grader that passes every explanation at 0.95 — the most generous a model can be. If the ceiling
/// holds against *this*, it holds. Every other AI-plane method is a harmless stub; only grading matters.
internal sealed class AlwaysPassesGrader : IAiPlane
{
    public Task<Result<TutorTurnResponse>> TutorTurnAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => Task.FromResult(Result<TutorTurnResponse>.Success(new TutorTurnResponse("stub", 0.0)));

    public async IAsyncEnumerable<string> TutorTurnStreamAsync(
        TutorTurnRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "stub";
        await Task.CompletedTask;
    }

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
        => Task.FromResult(Result<CheckClaimResponse>.Success(
            new CheckClaimResponse(ClaimOutcome.Wrong, null, 0.0)));

    public Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default)
        => Task.FromResult(Result<GradeResponse>.Success(
            new GradeResponse(GradeOutcome.Passed, 0.95, [], "A clear, complete explanation.", 3, false, false, 0.0)));

    public Task<Result<ItemResponse>> GenerateItemAsync(
        GenerateItemRequest request, CancellationToken ct = default)
        => Task.FromResult(Result<ItemResponse>.Success(
            new ItemResponse("item-1", "c", EvidenceType.Apply, "a question", 0.5, "fp")));
}
