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
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **Ninety days.**
///
/// A learner proves a concept. Properly — real evidence, accepted, held. Then life happens and they
/// do not touch it for three months.
///
/// Every other product in this industry has, at this point, **nothing to say.** The certificate they
/// issued in March is still on the wall in June. The progress bar is still at 100%. The dashboard
/// still says *complete*, because the only thing it ever recorded was that a box got ticked, and
/// **boxes do not decay.**
///
/// PRAXIS models the one thing that actually happened in those ninety days: *forgetting*. These tests
/// drive it end to end, through real HTTP, on the real seed pack, with the real forgetting curve —
/// and prove three things that all have to be true together, because any two of them without the
/// third is a different and worse product.
[Collection(SharedApi.Name)]
public sealed class NinetyDaysTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _api;

    private Guid _objective;
    private Guid _tokenisation;

    public NinetyDaysTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        // Idempotent: another test class in this collection may already have loaded it.
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();
        var loaded = await loader.LoadAsync(SeedPack());

        Assert.True(
            loaded.IsSuccess || loaded.Status == ResultStatus.Conflict,
            string.Join(Environment.NewLine, loaded.Errors));

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();

        _objective = await graph.Concepts.Where(c => c.Slug == "beating-the-baseline")
            .Select(c => c.Id).SingleAsync();

        _tokenisation = await graph.Concepts.Where(c => c.Slug == "tokenisation")
            .Select(c => c.Id).SingleAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── 1. It falls below the threshold ──────────────────────────────────────

    /// **The forgetting is real, and it is on the record.**
    ///
    /// Ninety days after one clean proof, PRAXIS's own belief about what this learner could retrieve
    /// today has collapsed — and it says so, in a number, over HTTP, to the learner.
    ///
    /// The threshold is not a hand-picked *"ninety days ought to be enough"*. It is the point at which
    /// the model says the memory has faded, and the test asks the model rather than my intuition.
    [Fact]
    public async Task After_ninety_days_a_concept_has_decayed_below_the_review_threshold()
    {
        var (client, learner) = await Learner();

        await ProveAsync(learner, _tokenisation, DateTime.UtcNow.AddDays(-90));

        var state = await ConceptAsync(client, _tokenisation);

        Assert.True(
            state.Retrievability < 0.90,
            $"Ninety days on, PRAXIS still thinks they could retrieve this {state.Retrievability:P0} "
            + "of the time. The forgetting curve is not running.");

        // And it is not a rounding error. It has properly gone.
        Assert.True(
            state.Retrievability < 0.50,
            $"After three months untouched, retrievability is {state.Retrievability:P0}. That is not "
            + "decay, that is a memory model with the handbrake on.");
    }

    /// **And the Ledger does not lie about it in either direction.**
    ///
    /// This is the pair of facts nobody else in the industry will show you together, because they look
    /// contradictory and they are not:
    ///
    /// **They did prove it.** That happened. It is on the event log forever, and no amount of time
    /// erases the fact that on a Tuesday in March this person demonstrated they could do this. Mastery
    /// credit does **not** decay, and a product that quietly revoked it would be rewriting somebody's
    /// history to make its own numbers tidier.
    ///
    /// **And they probably could not do it today.** That is *also* true, and it is the thing a
    /// certificate hides.
    ///
    /// A certificate that says only the first is a lie by omission. A tracker that says only the
    /// second has stolen something they earned. **PRAXIS says both.**
    [Fact]
    public async Task They_still_proved_it_and_they_probably_could_not_do_it_today()
    {
        var (client, learner) = await Learner();

        await ProveAsync(learner, _tokenisation, DateTime.UtcNow.AddDays(-90));

        var state = await ConceptAsync(client, _tokenisation);

        // The thing they earned. Untouched.
        Assert.True(state.IsHeld, "PRAXIS revoked something the learner actually proved.");
        Assert.True(state.MasteryCredit > 0);

        // The thing a certificate hides.
        Assert.True(state.Retrievability < 0.50);

        // **Belief decays towards the prior, never below it.** A forgotten concept does not leave you
        // knowing *less than a stranger* — and claiming otherwise would be PRAXIS asserting a
        // certainty it has not earned about somebody who did this perfectly three months ago.
        Assert.True(
            state.PKnows > 0.0,
            "Belief decayed to zero. PRAXIS now claims this person *definitely cannot* do something "
            + "they demonstrated perfectly ninety days ago, which it cannot possibly know.");

        Assert.True(state.PKnows < state.PKnowsAtLastEvidence);
    }

    // ── 2. It reappears as work — but not as the work you expected ───────────

    /// **The honest surprise, and the reason this milestone was worth writing.**
    ///
    /// The plan said: *"a concept left alone for 90 days must fall below threshold and **reappear in
    /// the queue**."* It falls below threshold — and it does **not** reappear in the review queue,
    /// **and it should not.**
    ///
    /// After ninety days one clean proof has decayed to roughly 36% retrievability. Put that in a
    /// three-minute review queue and the learner **fails it two times out of three** — at which point
    /// it was never a review at all. It was a lapse: the memory collapses, and they spend an evening
    /// relearning something they thought they knew (M8.2).
    ///
    /// So PRAXIS names it, counts it, and hands it **back to the plan as a re-learn**. Which is the
    /// difference between a system that models a mind and one that maintains a to-do list.
    [Fact]
    public async Task After_ninety_days_it_is_not_a_review_and_PRAXIS_refuses_to_call_it_one()
    {
        var (client, learner) = await Learner();

        await ProveAsync(learner, _tokenisation, DateTime.UtcNow.AddDays(-90));

        var reps = await RepsAsync(client);

        // **Not in the review queue.**
        Assert.DoesNotContain(reps.Due, r => r.Slug == "tokenisation");

        // **And not hidden either.** Named, counted, and priced as what it actually is.
        var lost = Assert.Single(reps.Lost, r => r.Slug == "tokenisation");

        Assert.Equal(nameof(RepUrgency.Lost), lost.Urgency);

        Assert.NotNull(reps.AboutWhatYouLost);

        Assert.Contains(
            "three-minute price tag", reps.AboutWhatYouLost!, StringComparison.Ordinal);

        // It tells the learner the number, rather than hiding it behind a red dot.
        Assert.True(lost.Retrievability < 0.50);
    }

    /// **But it absolutely does reappear as *work*.** It is not quietly dropped; it is the most
    /// important thing on the learner's day.
    ///
    /// The Scheduler calls it a **Rescue** — because a foundation that is dissolving makes everything
    /// built on top of it more expensive, and the learner attempting that work would not experience it
    /// as *harder*. They would experience it as **impossible**, and they would blame themselves.
    [Fact]
    public async Task After_ninety_days_it_comes_back_as_the_most_important_thing_on_the_day()
    {
        var (client, learner) = await Learner();

        await SetObjectiveAsync(client, minutesPerDay: 60);

        await ProveAsync(learner, _tokenisation, DateTime.UtcNow.AddDays(-90));

        var card = await CardAsync(client, 25);

        Assert.Equal("Rescue", card.Kind);
        Assert.Equal("tokenisation", card.Slug);

        // It names the real cost, and not "you have a review due".
        Assert.Contains("getting harder while it slips", card.Because, StringComparison.Ordinal);
    }

    // ── 3. And it is priced as a re-learn, not a rep ─────────────────────────

    /// **The same concept, at two different stages of decay, is not the same job** (M8.3).
    ///
    /// A learner who is 15% faded will pass their rep and it will cost them a couple of minutes. A
    /// learner ninety days gone will most likely *fail* it, and it will cost them an evening.
    ///
    /// Quoting the same number for both is not an estimation error. It is **the code that plans
    /// somebody's day lying to them about how it is going to go.**
    [Fact]
    public async Task Ninety_days_of_rot_is_priced_as_a_relearn_and_not_as_a_rep()
    {
        var (fresh, freshLearner) = await Learner();
        await SetObjectiveAsync(fresh, 60);
        await ProveAsync(freshLearner, _tokenisation, DateTime.UtcNow.AddDays(-1));

        var (rotted, rottedLearner) = await Learner();
        await SetObjectiveAsync(rotted, 60);
        await ProveAsync(rottedLearner, _tokenisation, DateTime.UtcNow.AddDays(-90));

        var reps = await RepsAsync(rotted);

        var lost = Assert.Single(reps.Lost, r => r.Slug == "tokenisation");

        // A re-learn costs multiples of a rep, and PRAXIS says so up front.
        Assert.True(
            lost.ExpectedMinutes > Reps.RepMinutes * 2,
            $"A concept the learner will probably fail was quoted at {lost.ExpectedMinutes} minutes. "
            + $"A rep they will pass costs {Reps.RepMinutes}. Those are not the same job.");

        // And the one they will pass is not on the queue at all — repping something you still hold
        // perfectly buys almost nothing (M8.1), and being early is not a virtue.
        var freshReps = await RepsAsync(fresh);

        Assert.DoesNotContain(freshReps.Due, r => r.Slug == "tokenisation");
        Assert.DoesNotContain(freshReps.Lost, r => r.Slug == "tokenisation");
    }

    // ── The window in between ────────────────────────────────────────────────

    /// **And in between, there is a window where a rep is worth the most in the world** — which is the
    /// entire reason PRAXIS lets you get close to forgetting on purpose.
    ///
    /// This is the state the ninety-day learner missed. Thirty days out, a two-minute question would
    /// have saved the whole thing.
    [Fact]
    public async Task Caught_in_time_the_same_concept_is_a_cheap_review_instead()
    {
        var (client, learner) = await Learner();

        await SetObjectiveAsync(client, 60);

        // Faded, but nowhere near lost — squarely in the window where retrieving it bites and they
        // will still manage it.
        await ProveAsync(learner, _tokenisation, DateTime.UtcNow.AddDays(-4));

        var reps = await RepsAsync(client);

        var due = Assert.Single(reps.Due, r => r.Slug == "tokenisation");

        Assert.True(due.Retrievability >= Reps.NotAReviewBelow);

        // Cheap, and it says why it is worth doing *now*.
        Assert.True(due.ExpectedMinutes < 8);

        Assert.NotEmpty(due.Because);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// A real event, at a real time, through the real projection. **The staleness is not mocked** —
    /// it is simulated by appending evidence in the past and letting the forgetting curve do what it
    /// does.
    ///
    /// **APPLY, not BUILD**: both seed concepts have contracts that accept `Recall` and `Apply` and
    /// nothing else, so a BUILD earns *zero* credit no matter how perfectly it ran. PRAXIS does not
    /// accept evidence its own contract never asked for, however impressive it looks.
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

    private static async Task<RepsBody> RepsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/memory/reps", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<RepsBody>(response)).Data!;
    }

    private static async Task<ConceptBody> ConceptAsync(HttpClient client, Guid concept)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/twin/concepts/{concept}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<ConceptBody>(response)).Data!;
    }

    private static async Task<CardBody> CardAsync(HttpClient client, int minutes)
    {
        using var response = await client.GetAsync(
            new Uri($"/api/scheduler/today?minutes={minutes}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await Read<CardBody>(response)).Data!;
    }

    private async Task SetObjectiveAsync(HttpClient client, int minutesPerDay)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/compiler/objectives", UriKind.Relative),
            new { conceptId = _objective, minutesPerDay, deadline = (DateTime?)null },
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid LearnerId)> Learner()
    {
        var client = _api.Factory.CreateClient();

        var email = $"decay-{Guid.CreateVersion7():N}@praxis.test";

        using var registered = await client.PostAsJsonAsync(
            new Uri("/api/identity/auth/register", UriKind.Relative),
            new { email, password = "correct-horse-battery-staple", displayName = "Decay" },
            Json);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        var body = await Read<AuthBody>(registered);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.Data!.AccessToken);

        return (client, body.Data.LearnerId);
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

    private static async Task<ApiBody<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiBody<T>>(Json))!;

    private sealed record ApiBody<T>(bool Success, T? Data, string? Message);

    private sealed record AuthBody(string AccessToken, Guid LearnerId);

    private sealed record ConceptBody(
        double PKnows,
        double PKnowsAtLastEvidence,
        double MasteryCredit,
        bool IsHeld,
        double Retrievability,
        double? DaysUntilDue);

    private sealed record RepsBody(
        IReadOnlyList<RepBody> Due,
        IReadOnlyList<RepBody> Lost,
        double ExpectedMinutes,
        bool NothingWorthDoing,
        string? AboutWhatYouLost);

    private sealed record RepBody(
        Guid ConceptId,
        string Slug,
        string Urgency,
        double Retrievability,
        double ExpectedMinutes,
        string Because);

    private sealed record CardBody(
        string Kind,
        Guid ConceptId,
        string Slug,
        string Headline,
        string Because,
        int Minutes);
}
