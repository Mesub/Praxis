using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;
using Praxis.Modules.Memory.Domain;

namespace Praxis.Api.Tests;

/// **The whole product, in one request.**
///
/// *"I have fifteen minutes. What do I do?"*
///
/// The graph, the twin, the forgetting curve and the compiler all exist so that this one endpoint
/// can answer that with **one card** instead of a dashboard. These tests drive it through real HTTP,
/// on the real seed pack, with real decay — the staleness is not mocked, it is *simulated by
/// appending evidence in the past and letting FSRS do what it does*.
///
/// The elapsed times below are derived from the model rather than guessed. A hand-picked "90 days
/// ought to be enough" is a test that passes for a reason you have not checked.
[Collection(SharedApi.Name)]
public sealed class SchedulerApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private Guid _objective;
    private Guid _tokenisation;
    private Guid _invertedIndex;

    /// The stability one clean APPLY confers, read out of the real Twin rather than assumed.
    private double _stability;

    public SchedulerApiTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var result = await loader.LoadAsync(SeedPack());

        Assert.True(result.IsSuccess || result.Status == ResultStatus.Conflict, string.Join("\n", result.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        _objective = graph.Concepts.Single(c => c.Slug == "beating-the-baseline").Id;
        _tokenisation = graph.Concepts.Single(c => c.Slug == "tokenisation").Id;
        _invertedIndex = graph.Concepts.Single(c => c.Slug == "the-inverted-index").Id;

        _stability = await DiscoverStabilityAsync();

        Assert.True(_stability > 0, "One clean apply conferred no stability at all.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Today_is_not_public()
    {
        using var client = _api.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/scheduler/today", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// **PRAXIS will not invent work.**
    ///
    /// A learner with no objective has no next step, and rather than hand them a card pointing
    /// nowhere — or, worse, a generated quiz to keep them here — the refusal travels intact.
    [Fact]
    public async Task Without_an_objective_there_is_no_card_and_none_is_invented()
    {
        var (client, _) = await Learner();

        using var response = await client.GetAsync(new Uri("/api/scheduler/today", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// A text box asking how long you have is planning by another name, and planning is the thing
    /// they came here to stop doing. Five, ten, fifteen, twenty-five. That is the menu.
    [Fact]
    public async Task A_session_is_one_of_four_lengths()
    {
        var (client, _) = await Learner();
        await SetObjectiveAsync(client, 60);

        using var odd = await Today(client, minutes: 7);
        Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);

        foreach (var length in new[] { 5, 10, 15, 25 })
        {
            using var ok = await Today(client, length);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
    }

    // ── Move forward ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_learner_is_given_the_first_thing_their_objective_requires()
    {
        var (client, _) = await Learner();
        await SetObjectiveAsync(client, 60);

        var card = await CardAsync(client, 15);

        Assert.Equal("Learn", card.Kind);
        Assert.Equal("tokenisation", card.Slug);

        // Never more time than they actually have.
        Assert.True(card.Minutes <= 15);

        // And they are owed a reason. A scheduler that cannot give one is guessing.
        Assert.Contains("Step 1 of your plan", card.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_card_is_one_action_and_never_a_list()
    {
        var (client, _) = await Learner();
        await SetObjectiveAsync(client, 60);

        var card = await CardAsync(client, 15);

        // One concept. One slug. One number of minutes. If this ever becomes an array, PRAXIS has
        // handed the hardest moment in learning — *starting* — back to the person who is
        // demonstrably bad at it.
        Assert.NotEqual(Guid.Empty, card.ConceptId);
        Assert.NotEmpty(card.Slug);
        Assert.NotEmpty(card.Headline);
    }

    // ── Reps ─────────────────────────────────────────────────────────────────

    /// Held, and starting to slip. Cheap to fix, so it is fixed — and *then* they move on.
    [Fact]
    public async Task Something_they_are_starting_to_forget_is_reviewed_before_new_work()
    {
        var (client, learner) = await Learner();
        await SetObjectiveAsync(client, 60);

        // Proved, then left alone until they would get it right about 80% of the time: past due,
        // but nowhere near lost.
        await ProveAsync(learner, _tokenisation, DaysAgoForRetrievability(0.80));

        var card = await CardAsync(client, 15);

        Assert.Equal("Rep", card.Kind);
        Assert.Equal("tokenisation", card.Slug);

        // Cheap. A rep is not a re-teach.
        Assert.True(card.Minutes <= 9, $"A rep on a 30-minute concept cost {card.Minutes} minutes.");
    }

    /// **A rep you are going to fail is not a three-minute rep** (M8.2, fixed in M8.3).
    ///
    /// The Scheduler used to price *every* due concept the same: a flat tenth of what it cost to learn
    /// it, clamped to 2–10 minutes. Including the ones the learner would fail seven times out of ten.
    ///
    /// A failed rep is not a review. It is a lapse — the memory collapses and they spend the evening
    /// relearning something they thought they knew. Quoting three minutes for that is not an
    /// estimation error, **it is a false statement about how their evening is going to go**, made by
    /// the code that builds their day.
    ///
    /// So the price is the *expected* one, and a concept they are probably going to fail is quoted at
    /// what it will really cost.
    [Fact]
    public async Task A_rep_the_learner_will_probably_fail_is_not_priced_like_one_they_will_pass()
    {
        var (safe, safeLearner) = await Learner();
        await SetObjectiveAsync(safe, 60);
        await ProveAsync(safeLearner, _tokenisation, DaysAgoForRetrievability(0.85));

        var cheap = await CardAsync(safe, 25);

        var (risky, riskyLearner) = await Learner();
        await SetObjectiveAsync(risky, 60);

        // Far enough gone that they will most likely fail it.
        await ProveAsync(riskyLearner, _tokenisation, DaysAgoForRetrievability(0.30));

        var expensive = await CardAsync(risky, 25);

        Assert.Equal("tokenisation", cheap.Slug);
        Assert.Equal("tokenisation", expensive.Slug);

        Assert.True(
            expensive.Minutes > cheap.Minutes,
            $"""
             The same concept was quoted at {cheap.Minutes} minutes when the learner would pass it 85%
             of the time, and {expensive.Minutes} minutes when they would pass it 30% of the time.

             Those are not the same job. The second one is a re-learn, and pricing it as a review is
             the code that plans somebody's evening lying to them about it.
             """);
    }

    /// **The trap most schedulers fall into, proved through the front door.**
    ///
    /// Clearing the rep queue first *feels* responsible. On a busy day the queue **is** the session,
    /// and the learner revises for weeks while the graph does not move — working hard, going nowhere,
    /// and eventually concluding, correctly, that this is pointless.
    ///
    /// So on a five-minute day, a rep that would eat most of it gives way to the plan.
    [Fact]
    public async Task On_a_short_day_a_rep_gives_way_to_moving_forward()
    {
        var (client, learner) = await Learner();
        await SetObjectiveAsync(client, 60);

        // A 40-minute concept: its rep costs 4 minutes, and 4 is more than 60% of a 5-minute session.
        await ProveAsync(learner, _invertedIndex, DaysAgoForRetrievability(0.80));

        var short_ = await CardAsync(client, 5);

        Assert.False(
            short_.Kind == "Rep",
            "In a 5-minute session the scheduler spent it on a rep. The learner is now revising "
            + "instead of learning, and will keep doing so forever.");

        Assert.Equal("Learn", short_.Kind);

        // The rep did not vanish — it was deferred, and the deferral says why.
        var deferred = short_.AlsoDue.Single(d => d.Slug == "the-inverted-index");
        Assert.Contains("the plan cannot wait forever", deferred.Reason, StringComparison.Ordinal);

        // And it is a *rep* being deferred, not a foundation collapsing. Nothing is dissolving here,
        // and the card must not imply otherwise.
        Assert.DoesNotContain("ground under it is solid", deferred.Reason, StringComparison.Ordinal);

        // And on a longer day, the very same rep is taken. The cap is a cap, not a refusal.
        var longer = await CardAsync(client, 25);

        Assert.Equal("Rep", longer.Kind);
        Assert.Equal("the-inverted-index", longer.Slug);
    }

    // ── Rescue ───────────────────────────────────────────────────────────────

    /// **A dissolving foundation is not "due" — it is load-bearing.**
    ///
    /// Everything above it in the graph now costs multiples more, and the learner attempting that
    /// work will not experience it as harder. They will experience it as *impossible*, and they will
    /// blame themselves.
    [Fact]
    public async Task A_foundation_that_is_dissolving_beats_everything_else_on_the_day()
    {
        var (client, learner) = await Learner();
        await SetObjectiveAsync(client, 60);

        // Long enough that they would get it right about 40% of the time. It is going.
        await ProveAsync(learner, _tokenisation, DaysAgoForRetrievability(0.40));

        var card = await CardAsync(client, 15);

        Assert.Equal("Rescue", card.Kind);
        Assert.Equal("tokenisation", card.Slug);

        // The reason names the real cost, not "you have a review due".
        Assert.Contains("getting harder while it slips", card.Because, StringComparison.Ordinal);

        // **And nothing is deferred behind it — which surprised me, and then convinced me.**
        //
        // The Compiler, reasoning entirely independently, has priced the rotted concept as a
        // `Refresh` step and put it at the front of the plan. So the plan's next step and the thing
        // being rescued are the *same concept*. Two engines that do not know about each other, given
        // the same learner, arrived at the same answer — and there is genuinely nothing else worth
        // doing today.
        Assert.Empty(card.AlsoDue);
    }

    /// Rescue is allowed to take the whole session. Building on a foundation that is dissolving is
    /// worse than not building at all.
    [Fact]
    public async Task A_rescue_outranks_the_rep_cap()
    {
        var (client, learner) = await Learner();
        await SetObjectiveAsync(client, 60);

        await ProveAsync(learner, _invertedIndex, DaysAgoForRetrievability(0.30));

        // Five minutes — the cap would have refused an ordinary 4-minute rep here.
        var card = await CardAsync(client, 5);

        Assert.Equal("Rescue", card.Kind);
        Assert.Equal("the-inverted-index", card.Slug);
    }

    // ── Nothing ──────────────────────────────────────────────────────────────

    /// **PRAXIS optimises for the learner leaving.**
    ///
    /// The objective is reached and nothing is rotting. No invented quiz. No streak to protect. An
    /// empty day is a real state, and it gets said out loud — which is precisely the thing a product
    /// measured on engagement could never ship.
    [Fact]
    public async Task When_there_is_nothing_worth_doing_PRAXIS_says_so_rather_than_inventing_work()
    {
        var (client, learner) = await Learner();

        // An objective they have already met: one root concept, proved today.
        await SetObjectiveAsync(client, 60, objective: _tokenisation);
        await ProveAsync(learner, _tokenisation, DateTime.UtcNow);

        var card = await CardAsync(client, 25);

        Assert.Equal("Nothing", card.Kind);
        Assert.Equal(0, card.Minutes);
        Assert.Empty(card.AlsoDue);

        Assert.Contains("will not invent some", card.Because, StringComparison.Ordinal);
        Assert.Contains("the point was never the app", card.Because, StringComparison.Ordinal);
    }

    // ── Isolation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_learner_only_ever_gets_their_own_card()
    {
        var (alice, aliceId) = await Learner();
        var (bob, _) = await Learner();

        await SetObjectiveAsync(alice, 60);
        await SetObjectiveAsync(bob, 60);

        await ProveAsync(aliceId, _tokenisation, DaysAgoForRetrievability(0.40));

        Assert.Equal("Rescue", (await CardAsync(alice, 15)).Kind);

        // Bob's day is untouched by Alice's rot.
        var bobs = await CardAsync(bob, 15);

        Assert.Equal("Learn", bobs.Kind);
        Assert.Empty(bobs.AlsoDue);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// The moment at which a concept proved once would have decayed to `retrievability`.
    ///
    /// Derived from the curve, not guessed at. If the FSRS constants ever change, these tests move
    /// with them instead of quietly starting to prove something else.
    private DateTime DaysAgoForRetrievability(double retrievability)
        => DateTime.UtcNow.AddDays(-Fsrs.Interval(_stability, retrievability));

    /// What one clean APPLY is actually worth, read back out of the real Twin. Assuming it would be
    /// hardcoding the model into the test of the model.
    private async Task<double> DiscoverStabilityAsync()
    {
        var probe = Guid.CreateVersion7();

        await ProveAsync(probe, _tokenisation, DateTime.UtcNow);

        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var twin = scope.ServiceProvider.GetRequiredService<TwinDbContext>();

        var state = await twin.States
            .AsNoTracking()
            .SingleAsync(s => s.LearnerId == probe && s.ConceptId == _tokenisation);

        return state.Stability;
    }

    /// A real event, at a real time, through the real projection. Nothing here is a fixture.
    ///
    /// **APPLY, not BUILD** — and that is not a detail. Both concepts used here have contracts that
    /// accept `Recall` and `Apply` and nothing else, so a BUILD on them earns *zero* credit no matter
    /// how perfectly it ran. The first draft of these tests appended BUILDs, nothing was ever held,
    /// and six scheduler tests failed. They were right to: PRAXIS does not accept evidence its own
    /// contract never asked for, however impressive it looks.
    private async Task ProveAsync(Guid learner, Guid concept, DateTime at)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var appended = await store.AppendAsync(
            learner,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, Score: 1.0, Assurance: 1.0),
            at,
            concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private static Task<HttpResponseMessage> Today(HttpClient client, int minutes)
        => client.GetAsync(new Uri($"/api/scheduler/today?minutes={minutes}", UriKind.Relative));

    private static async Task<CardBody> CardAsync(HttpClient client, int minutes)
    {
        using var response = await Today(client, minutes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<CardBody>(response)).Data!;
    }

    private async Task SetObjectiveAsync(HttpClient client, int minutesPerDay, Guid? objective = null)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/compiler/objectives", UriKind.Relative),
            new { conceptId = objective ?? _objective, minutesPerDay, deadline = (DateTime?)null },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid LearnerId)> Learner()
    {
        var client = _api.Factory.CreateClient();

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new
            {
                email = $"sched-{Guid.CreateVersion7():N}@example.com",
                displayName = "Scheduler",
                password = "a good long passphrase",
            },
            Json);

        var auth = (await Read<AuthBody>(registered)).Data!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth.LearnerId);
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

    private sealed record AuthBody(Guid LearnerId, string Email, string DisplayName, string AccessToken, DateTime ExpiresAt);

    private sealed record CardBody(
        string Kind,
        Guid ConceptId,
        string Slug,
        string Headline,
        string Because,
        int Minutes,
        IReadOnlyList<DeferredBody> AlsoDue);

    private sealed record DeferredBody(Guid ConceptId, string Slug, string Reason, int Minutes);
}
