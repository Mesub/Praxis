using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;

namespace Praxis.Api.Tests;

/// **Nobody leaves empty-handed — enforced on the way *out* of the log, not just on the way in.**
///
/// The domain refuses to complete an empty session. This proves the *projection* refuses too: if a
/// buggy tutor appends a `SessionCompleted` for a session in which nothing was made, the projection
/// re-derives the refusal and the session stays **open** rather than being recorded as a success
/// that never happened.
///
/// A rule enforced only at the write path is a rule one bad writer can bypass.
[Collection(SharedApi.Name)]
public sealed class SessionProjectionTests
{
    private readonly ApiFixture _api;

    public SessionProjectionTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_session_is_rebuilt_from_what_the_learner_actually_did()
    {
        var (learner, concept, session) = Ids();

        await Append(learner, concept, new SessionStarted(session, PlannedMinutes: 15));
        await Append(learner, concept, new HintTaken(session, HintRung.Probe, 114));
        await Append(learner, concept, new ProductionRecorded(session, ProductionKind.SelfExplanation));
        await Append(learner, concept, new SessionCompleted(session, ActualMinutes: 14, EvidenceProduced: 1));

        var stored = await SessionAsync(session);

        Assert.Equal(1, stored.HintsTaken);
        Assert.Equal(1, stored.Produced);
        Assert.True(stored.MadeSomething);
        Assert.Equal(SessionState.Completed, stored.State);
    }

    /// **The bad writer.**
    ///
    /// A tutor with a bug appends `SessionCompleted` for a session in which the learner took three
    /// hints and made nothing. The projection re-derives the rule and **refuses** — the session stays
    /// open, and PRAXIS does not have a completed session in its history that never happened.
    [Fact]
    public async Task A_completion_event_cannot_close_a_session_in_which_nothing_was_produced()
    {
        var (learner, concept, session) = Ids();

        await Append(learner, concept, new SessionStarted(session, 15));
        await Append(learner, concept, new HintTaken(session, HintRung.Probe, 114));
        await Append(learner, concept, new HintTaken(session, HintRung.Analogy, 100));
        await Append(learner, concept, new HintTaken(session, HintRung.WorkedExample, 72));

        // The lie.
        await Append(learner, concept, new SessionCompleted(session, 15, EvidenceProduced: 4));

        var stored = await SessionAsync(session);

        Assert.False(stored.MadeSomething);

        Assert.Equal(
            SessionState.Open,
            stored.State);

        Assert.Null(stored.Outcome);
    }

    /// **The stuck note, end to end.** Out of budget, blocked — and they *still made something*.
    [Fact]
    public async Task A_blocked_learner_leaves_having_produced_a_stuck_note()
    {
        var (learner, concept, session) = Ids();

        await Append(learner, concept, new SessionStarted(session, 25));
        await Append(learner, concept, new HintTaken(session, HintRung.PartialScaffold, 75));

        await Append(learner, concept, new AnswerBudgetExhausted(session, HintRung.PartialScaffold, 1));
        await Append(learner, concept, new StuckNoteWritten(
            session, "I can see TF saturates but I can't see why length normalisation is separate."));

        await Append(learner, concept, new SessionCompleted(session, 24, 0));

        var stored = await SessionAsync(session);

        Assert.Equal(1, stored.StuckNotes);
        Assert.True(stored.MadeSomething);

        Assert.Equal(SessionState.Completed, stored.State);
        Assert.Equal(SessionOutcome.BlockedUnresolved, stored.Outcome);
    }

    [Fact]
    public async Task A_learner_who_walks_away_is_recorded_and_not_chased()
    {
        var (learner, concept, session) = Ids();

        await Append(learner, concept, new SessionStarted(session, 15));
        await Append(learner, concept, new HintTaken(session, HintRung.Probe, 114));
        await Append(learner, concept, new SessionAbandoned(session, MinutesIn: 4, AtStep: "probe"));

        var stored = await SessionAsync(session);

        Assert.Equal(SessionState.Abandoned, stored.State);
        Assert.Equal(SessionOutcome.Abandoned, stored.Outcome);
    }

    /// The standing proof (M1.6): destroy it, replay history, get the same session back.
    [Fact]
    public async Task A_session_survives_being_destroyed_and_rebuilt_from_history()
    {
        var (learner, concept, session) = Ids();

        await Append(learner, concept, new SessionStarted(session, 15));
        await Append(learner, concept, new HintTaken(session, HintRung.Analogy, 106));
        await Append(learner, concept, new ProductionRecorded(session, ProductionKind.TeachBack));

        var before = await SessionAsync(session);

        await using (var scope = _api.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().RebuildAsync();
        }

        var after = await SessionAsync(session);

        Assert.Equal(before.Produced, after.Produced);
        Assert.Equal(before.HintsTaken, after.HintsTaken);
        Assert.Equal(before.MadeSomething, after.MadeSomething);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static (Guid Learner, Guid Concept, Guid Session) Ids()
        => (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private async Task Append<TPayload>(Guid learner, Guid concept, TPayload payload)
        where TPayload : IEventPayload
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var appended = await store.AppendAsync(learner, payload, DateTime.UtcNow, concept);

        Assert.True(appended.IsSuccess, appended.Message);

        await scope.ServiceProvider.GetRequiredService<ProjectionRunner>().CatchUpAsync();
    }

    private async Task<Session> SessionAsync(Guid sessionId)
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TutorDbContext>();

        return await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
    }
}
