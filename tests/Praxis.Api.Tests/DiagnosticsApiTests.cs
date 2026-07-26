using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The HTTP door for a fixed-key answer.**
///
/// `SubmitAnswerTests.cs` (`Praxis.Modules.Assessment.Tests`) proves `SubmitAnswer.Handler`'s own
/// rules — exactly-one, shape, range — by calling it directly. This file proves something the review
/// of Task 8 found missing: that a real request over HTTP can actually reach it.
///
/// `DiagnosticsController.Answer` builds `SubmitAnswer.Command` **by hand** rather than binding it
/// (the file's own M6.5b comment explains why: binding a `Command` directly is what lets
/// `ValidationFilter` find its validator, and this action binds `AnswerRequest` instead). That means
/// `SubmitAnswer.Validator`'s exactly-one rule never runs on a live request — only
/// `AnswerRequestValidator`'s copy of it does. Before this file existed, nothing proved that DTO
/// actually carried `selectedOption` at all: it was silently dropped by model binding, and every
/// fixed-key POST died as "an empty answer is not an answer" before the handler ever ran.
[Collection(SharedApi.Name)]
public sealed class DiagnosticsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    public DiagnosticsApiTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_fixed_key_answer_posted_with_a_selection_reaches_the_handler_and_comes_back_marked()
    {
        var ai = new StubAiPlane(ClaimOutcome.Correct, misconception: null);
        using var factory = BuildFactory(ai);

        var client = await RegisteredClient(factory);
        var itemId = await BankFixedKeyItem(factory);

        using var response = await client.PostAsJsonAsync(
            AnswersUri(Guid.CreateVersion7()),
            new { itemId, selectedOption = 1, statedConfidence = 0.85, seconds = 30 },
            Json);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var answer = (await Read<AnswerBody>(body)).Data!;
        Assert.Equal("Correct", answer.Outcome);

        // Not merely a 200 — the selection genuinely reached the oracle, and nothing else did.
        var checkedClaim = Assert.Single(ai.Asked);
        Assert.Equal(1, checkedClaim.SelectedOption);
        Assert.Equal(string.Empty, checkedClaim.Claim);
    }

    [Fact]
    public async Task The_door_refuses_a_body_carrying_both_a_claim_and_a_selection()
    {
        var ai = new StubAiPlane(ClaimOutcome.Correct, misconception: null);
        using var factory = BuildFactory(ai);

        var client = await RegisteredClient(factory);
        var itemId = await BankFixedKeyItem(factory);

        using var response = await client.PostAsJsonAsync(
            AnswersUri(Guid.CreateVersion7()),
            new
            {
                itemId,
                claim = "I think it is B",
                selectedOption = 1,
                statedConfidence = 0.85,
                seconds = 30,
            },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused at the door — the oracle was never asked.
        Assert.Empty(ai.Asked);
    }

    [Fact]
    public async Task The_door_refuses_a_body_carrying_neither_a_claim_nor_a_selection()
    {
        var ai = new StubAiPlane(ClaimOutcome.Correct, misconception: null);
        using var factory = BuildFactory(ai);

        var client = await RegisteredClient(factory);
        var itemId = await BankFixedKeyItem(factory);

        using var response = await client.PostAsJsonAsync(
            AnswersUri(Guid.CreateVersion7()),
            new { itemId, statedConfidence = 0.85, seconds = 30 },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(ai.Asked);
    }

    // ── Plumbing ───────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> BuildFactory(IAiPlane aiPlane)
        => _api.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiPlane>();

                // One instance across every request in the test, so `Asked` actually accumulates —
                // the ordinary `AddScoped<IAiPlane, T>` shape would hand out a fresh, empty one per
                // DI scope and lose everything the moment the request that captured it ended.
                services.AddScoped<IAiPlane>(_ => aiPlane);
            }));

    private static async Task<HttpClient> RegisteredClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"diagnostics-api-{Guid.CreateVersion7():N}@example.com",
                displayName = "Diagnostics",
                password = "a good long passphrase",
            },
            Json);

        var auth = (await Read<AuthBody>(await registered.Content.ReadAsStringAsync())).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return client;
    }

    /// Banked directly against the real schema through the host's own DI — no HTTP endpoint puts a
    /// fixed-key item in the bank on demand, and this file is testing the *answer* door, not the
    /// generation path `ItemBankE2ETests.cs` already covers.
    private static async Task<Guid> BankFixedKeyItem(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();

        var id = Guid.CreateVersion7();

        var item = BankedItem.From(
            id,
            Guid.CreateVersion7(),
            EvidenceType.Apply,
            stem: "Which sentence best restates the claim in the passage?",
            fingerprint: id.ToString("N"),
            difficulty: 0.0,
            guessing: 0.25,
            format: ItemFormat.MultipleChoice,
            passage: "The memo said the new schedule applies to everyone working weekends.",
            options:
            [
                "Everyone who worked a Saturday shift last month.",
                "Only staff hired after the memo went out.",
                "Nobody — the memo does not name anyone.",
                "The manager who wrote the memo.",
            ]);

        db.Items.Add(item);
        await db.SaveChangesAsync();

        return id;
    }

    private static Uri AnswersUri(Guid sessionId) => new(
        $"/api/assessment/diagnostics/{sessionId}/answers", UriKind.Relative);

    private static Task<ApiResponse<T>> Read<T>(string body)
        => Task.FromResult(JsonSerializer.Deserialize<ApiResponse<T>>(body, Json)!);

    private sealed record AuthBody(
        Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record AnswerBody(
        string Outcome, string? Misconception, bool YouSaidYouWereSure, string AndYouWere);
}

/// A scripted oracle for exactly what this file needs: one scripted verdict, and every request it
/// was actually asked to check — captured so a test can prove what reached the wire, not just that
/// something did.
internal sealed class StubAiPlane : IAiPlane
{
    private readonly ClaimOutcome _outcome;
    private readonly string? _misconception;

    public StubAiPlane(ClaimOutcome outcome, string? misconception = null)
        => (_outcome, _misconception) = (outcome, misconception);

    public List<CheckClaimRequest> Asked { get; } = [];

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Asked.Add(request);

        return Task.FromResult(Result<CheckClaimResponse>.Success(
            new CheckClaimResponse(_outcome, _misconception, Usd: 0.0004)));
    }

    public Task<Result<ItemResponse>> GenerateItemAsync(
        GenerateItemRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not exercised by this journey.");

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
