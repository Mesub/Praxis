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
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The end-to-end proof (M13.3) — that Phase 0 is one product, not eight demos.**
///
/// Every engine has its own tests. This is the different thing: a single learner, one continuous
/// script, whose *state flows between the engines through the front door*. It is the proof that the
/// handoffs hold — that evidence the kernel records actually moves the Twin, that a Twin that now
/// holds a concept actually changes the Compiler's plan — because a system where each part passes its
/// own tests but the seams leak is a system that works only in the test suite.
///
/// **This part (M13.3a) is the front of the journey: register → objective → plan → prove the first
/// concept → watch it land in the Twin → watch the plan recompile around it.** The later thirds add
/// doing the work through to reps (b) and the Reality Loop through to the signed Ledger record (c).
///
/// ## One honest note about how evidence is produced here
///
/// Phase 0 kept the 30-concept pack and deferred the item bank (M13.1). The item-driven *diagnostic*
/// reads that bank and — correctly — refuses to invent an item when it is empty, and nothing yet
/// generates-and-banks items. So this journey produces evidence the way the rest of the system will
/// consume it once generation is wired: as an accepted-evidence fact on the immutable log (the same
/// path `NinetyDaysTests` uses). The engines downstream of the item — Twin, Compiler, Memory, Reality,
/// Ledger — are exercised through real HTTP exactly as a learner would drive them.
[Collection(SharedApi.Name)]
public sealed class EndToEndJourneyTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    // A host with two stubs — the AI plane (the tutor turn has no real Claude to call in a test) and
    // the sandbox (a code Loop resolves by *running* code; the stub prints "4" deterministically, so
    // the test needs no Docker). The point here is the *journey* wiring, not the model or the runtime.
    // It shares the fixture's database, so evidence written through a scope and state read over HTTP
    // are the same learner.
    private WebApplicationFactory<Program> _factory = null!;

    private Guid _objective;      // beating-the-baseline — the pack's one build target
    private Guid _frontier;       // tokenisation — the single place a learner can start
    // Three concepts whose contract a single unaided APPLY satisfies on its own (Apply max ≥ hold,
    // count 1) — so one accepted APPLY carries each to HELD, as the Evidence Ceiling's floor promises.
    private Guid _held1;          // the-inverted-index (0.75 / 0.75)
    private Guid _held2;          // vector-similarity (0.80 / 0.80)
    private Guid _held3;          // ranking-metrics (0.80 / 0.80)

    public EndToEndJourneyTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        _factory = _api.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiPlane>();
                services.AddScoped<IAiPlane, LeakingAiPlane>();
                services.RemoveAll<ISandbox>();
                services.AddScoped<ISandbox>(_ => new StubSandbox(
                    Result<ExecutionResult>.Success(
                        new ExecutionResult(ExecutionOutcome.Passed, 0, "4\n", "", 5))));
            }));

        await using var scope = _api.Factory.Services.CreateAsyncScope();

        // Idempotent: another class in this collection may already have loaded the real pack.
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var loaded = await loader.LoadAsync(SeedPack());
        Assert.True(
            loaded.IsSuccess || loaded.Status == ResultStatus.Conflict,
            string.Join(Environment.NewLine, loaded.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        _objective = graph.Concepts.Single(c => c.Slug == "beating-the-baseline").Id;
        _frontier = graph.Concepts.Single(c => c.Slug == "tokenisation").Id;
        _held1 = graph.Concepts.Single(c => c.Slug == "the-inverted-index").Id;
        _held2 = graph.Concepts.Single(c => c.Slug == "vector-similarity").Id;
        _held3 = graph.Concepts.Single(c => c.Slug == "ranking-metrics").Id;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task From_registration_to_a_believable_plan_that_recompiles_around_proven_evidence()
    {
        // ── Stage 1: register ────────────────────────────────────────────────
        var (client, learner) = await Register();

        // ── Stage 2: state the objective, and get the plan ───────────────────
        await SetObjective(client, minutesPerDay: 90);

        var plan = await GetPlan(client);

        // The plan is the whole 30-concept path, and it is honest: it starts where a learner who
        // knows nothing can start, and it ends at the one thing the pack exists to reach.
        Assert.True(plan.Believable, "A 90-minute-a-day plan for the seed pack should be believable.");
        Assert.Equal(30, plan.Steps.Count);
        Assert.Equal("tokenisation", plan.Steps[0].Slug);
        Assert.Equal("beating-the-baseline", plan.Steps[^1].Slug);
        Assert.Equal(0, plan.ConceptsAlreadyHeld);
        Assert.True(plan.Wall.Reachable, "The objective must be reachable from nothing.");

        // ── Stage 3: prove the frontier concept, for real ────────────────────
        // Unaided APPLY evidence — the kind a model cannot fabricate and the kind that is allowed to
        // cross the hold threshold on its own (the Evidence Ceiling's floor).
        await ProveConcept(learner, _frontier);

        // ── Stage 4: the Twin now holds it — the evidence actually moved belief ─
        var held = await GetConcept(client, _frontier);
        Assert.True(held.IsHeld, "Accepted APPLY evidence should have carried tokenisation to HELD.");
        Assert.True(
            held.Retrievability > 0.90,
            $"Freshly proved, retrievability should be near 1.0, not {held.Retrievability:P0}.");
        Assert.True(held.MasteryCredit > 0, "Holding a concept means it carries mastery credit.");

        // ── Stage 5: the plan recompiles around what is now known ────────────
        // This is the seam that matters: the Compiler reads the Twin, so proving one concept must
        // shrink the road to the objective — not leave the learner re-learning what they hold.
        var recompiled = await GetPlan(client);

        Assert.True(
            recompiled.ConceptsAlreadyHeld >= 1,
            "The recompiled plan should count tokenisation among what is already held.");
        Assert.DoesNotContain(
            recompiled.Steps,
            s => s.Slug == "tokenisation" && s.Kind == "Learn");
        Assert.True(
            recompiled.Steps.Count < plan.Steps.Count,
            $"Proving a concept should leave fewer to learn ({recompiled.Steps.Count} vs {plan.Steps.Count}).");
        Assert.True(recompiled.Believable, "Removing work should not make the plan less believable.");
        Assert.Equal("beating-the-baseline", recompiled.Steps[^1].Slug);
    }

    [Fact]
    public async Task The_middle_the_plan_is_worked_a_session_runs_and_reps_reflect_what_is_due()
    {
        var (client, learner) = await Register();
        await SetObjective(client, minutesPerDay: 90);

        var start = await GetPlan(client);
        Assert.Equal(0, start.ConceptsAlreadyHeld);

        // ── Work the plan: prove three concepts, for real, now ───────────────
        await ProveConcept(learner, _held1);
        await ProveConcept(learner, _held2);
        await ProveConcept(learner, _held3);

        // The Twin holds all three, and the plan shrinks by exactly three — the Compiler is reading
        // the Twin, not a checklist of its own.
        foreach (var c in new[] { _held1, _held2, _held3 })
        {
            Assert.True((await GetConcept(client, c)).IsHeld);
        }

        var worked = await GetPlan(client);
        Assert.True(worked.ConceptsAlreadyHeld >= 3, $"Held {worked.ConceptsAlreadyHeld}, expected ≥ 3.");
        Assert.Equal(start.Steps.Count - 3, worked.Steps.Count);
        Assert.True(worked.Believable);

        // Freshly proved, nothing is due yet — a rep queue that nagged you the day you learned it
        // would be a streak counter wearing a lab coat.
        var fresh = await Reps(client);
        Assert.DoesNotContain(fresh.Due.Concat(fresh.Lost), r => r.ConceptId == _held1);

        // ── A tutor session runs, start to finish ────────────────────────────
        // Start, take the free rung (the tutor, which has no answer), then *make something* — a
        // session cannot be completed on hints alone (that would mistake fluency for knowledge), so
        // producing is what earns the right to finish.
        var session = await StartSession(client, _frontier);
        await TakeHint(client, session);
        await Produce(client, session, "selfExplanation");
        await EndSession(client, session, actualMinutes: 12);

        // ── Time passes on one concept, and reps says so ─────────────────────
        // The same concept, proved long enough ago that the forgetting curve has pulled it under.
        // The Memory engine reads the Twin's decay, so the rep queue must surface it — as a review if
        // it is merely slipping, or as a loss if it has fallen too far to be a review at all.
        await ProveConcept(learner, _frontier, DateTime.UtcNow.AddDays(-120));

        var later = await Reps(client);
        Assert.Contains(
            later.Due.Concat(later.Lost),
            r => r.ConceptId == _frontier);
    }

    [Fact]
    public async Task The_back_a_reality_loop_resolves_and_the_ledger_signs_what_was_proved()
    {
        var (client, learner) = await Register();

        // Something proved, so the record has a competency to carry — unaided APPLY, full assurance.
        await ProveConcept(learner, _held1);

        // ── A Reality Loop: a falsifiable claim, committed before the world settles it ─
        var loop = await CommitLoop(client, code: "print(2 + 2)", answer: "4", confidence: 0.8);

        // Reality answers. The sandbox runs the code, prints 4, and the claim was right — so this is
        // calibration evidence, scored the same day (R-4), not a grade a model handed out.
        var verdict = await ResolveLoop(client, loop);
        Assert.True(verdict.Correct, "The code prints 4 and the learner predicted 4.");
        Assert.True(verdict.BrierScore < 0.25, $"A confident, correct call should score well; got {verdict.BrierScore:F2}.");

        // ── The Ledger issues the signed record of what was actually proved ──
        var record = await GetLedger(client);

        // It is a real signed document: signature and the public key a verifier checks it against.
        Assert.False(string.IsNullOrEmpty(record.Signature), "An unsigned record proves nothing.");
        Assert.False(string.IsNullOrEmpty(record.PublicKey), "A record without its key cannot be verified.");

        // And it says exactly what was proved — the held competency, and nothing invented.
        var competency = Assert.Single(record.Competencies);
        Assert.Equal(_held1, competency.ConceptId);
        Assert.Equal("The inverted index", competency.ConceptName);

        // Honest assurance: this came from unaided APPLY — work a model could not fake — so it is
        // carried at full trust, un-discounted (C-6).
        Assert.True(
            competency.Assurance >= 0.99,
            $"APPLY evidence should be recorded at full assurance, not {competency.Assurance:F2}.");
    }

    // ── The script's steps, each a real HTTP call a learner would make ───────

    private async Task<Guid> CommitLoop(HttpClient client, string code, string answer, double confidence)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/reality/loops", UriKind.Relative),
            new { kind = "output", code, answer, confidence },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<CommitBody>(response)).Data!.LoopId;
    }

    private static async Task<ResolveBody> ResolveLoop(HttpClient client, Guid loop)
    {
        using var response = await client.PostAsync(
            new Uri($"/api/reality/loops/{loop}/resolve", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<ResolveBody>(response)).Data!;
    }

    private static async Task<RecordBody> GetLedger(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/ledger/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<RecordBody>(response)).Data!;
    }

    private static async Task<RepsBody> Reps(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/memory/reps", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<RepsBody>(response)).Data!;
    }

    private async Task<Guid> StartSession(HttpClient client, Guid concept)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/tutor/sessions", UriKind.Relative),
            new { conceptId = concept, plannedMinutes = 15 },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<SessionBody>(response)).Data!.SessionId;
    }

    private static async Task TakeHint(HttpClient client, Guid session)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/tutor/sessions/{session}/hints", UriKind.Relative),
            new { rung = "orient", learnerSaid = "I am not sure where to begin." },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task Produce(HttpClient client, Guid session, string kind)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/tutor/sessions/{session}/productions", UriKind.Relative),
            new { kind },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task EndSession(HttpClient client, Guid session, int actualMinutes)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/tutor/sessions/{session}/end", UriKind.Relative),
            new { abandoned = false, actualMinutes },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    private async Task<(HttpClient Client, Guid Learner)> Register()
    {
        var client = _factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"journey-{Guid.CreateVersion7():N}@example.com",
                displayName = "Journey",
                password = "a good long passphrase",
            },
            Json);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth.LearnerId);
    }

    private async Task SetObjective(HttpClient client, int minutesPerDay)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/compiler/objectives", UriKind.Relative),
            new { conceptId = _objective, minutesPerDay, deadline = (DateTime?)null },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlanBody> GetPlan(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/compiler/plan", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<PlanBody>(response)).Data!;
    }

    /// Accepted, unaided APPLY evidence, on the log — the fact the diagnostic would have produced.
    /// `at` defaults to now; pass a past date to let the forgetting curve run against it.
    private async Task ProveConcept(Guid learner, Guid concept, DateTime? at = null)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var appended = await store.AppendAsync(
            learner,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, Score: 1.0, Assurance: 1.0),
            at ?? DateTime.UtcNow,
            concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private static async Task<ConceptBody> GetConcept(HttpClient client, Guid concept)
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

    private sealed record PlanBody(
        Guid Objective,
        IReadOnlyList<StepBody> Steps,
        double TotalMinutes,
        int ConceptsAlreadyHeld,
        int ConceptsNeedingRefresh,
        double DaysAtStatedPace,
        bool Believable,
        WallBody Wall);

    private sealed record StepBody(
        Guid ConceptId, string Slug, int Position,
        double Minutes, double CumulativeMinutes, int DayReached, string Kind);

    private sealed record WallBody(
        bool Reachable, int ConceptsReached, int ConceptsInPlan,
        int DaysToObjective, int HorizonDays,
        double PeakMaintenanceMinutesPerDay, double MaintenanceShareOfDay);

    private sealed record ConceptBody(
        double PKnows,
        double PKnowsAtLastEvidence,
        double MasteryCredit,
        bool IsHeld,
        double Retrievability,
        double? DaysUntilDue);

    private sealed record SessionBody(
        Guid SessionId, Guid ConceptId, string State, string? Outcome,
        int BudgetRemaining, int NextRungCost, string? NextRung,
        int HintsTaken, int Produced, bool MadeSomething, bool AwaitingYou);

    private sealed record RepsBody(
        IReadOnlyList<RepBody> Due,
        IReadOnlyList<RepBody> Lost,
        double ExpectedMinutes,
        bool NothingWorthDoing,
        string? AboutWhatYouLost);

    private sealed record RepBody(
        Guid ConceptId, string Slug, string Urgency,
        double Retrievability, double ExpectedMinutes, string Because);

    private sealed record CommitBody(Guid LoopId, string Kind, string Answer, double Confidence);

    private sealed record ResolveBody(
        Guid LoopId, bool Correct, double BrierScore, string Predicted, string GroundTruth, double Confidence);

    /// A sandbox that "runs" the code deterministically — so a code Loop resolves without Docker.
    private sealed class StubSandbox(Result<ExecutionResult> outcome) : ISandbox
    {
        public Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
            => Task.FromResult(outcome);
    }

    private sealed record CompetencyBody(Guid ConceptId, string ConceptName, double Assurance);

    private sealed record RecordBody(
        string Subject,
        IReadOnlyList<CompetencyBody> Competencies,
        string Signature,
        string PublicKey,
        DateTime IssuedAt);
}
