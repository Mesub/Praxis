using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Tests;

[Collection(SharedPostgres.Name)]
public sealed class TypedAppendTests
{
    private readonly PostgresFixture _postgres;

    public TypedAppendTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_typed_payload_appends_and_rehydrates()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var appended = await store.AppendAsync(
            learner,
            new EvidenceAccepted(
                EvidenceId: Guid.CreateVersion7(),
                Type: EvidenceType.Build,
                Score: 1.0,
                Assurance: 0.95),
            DateTime.UtcNow,
            concept);

        Assert.True(appended.IsSuccess);

        // The event type was taken from the payload — the caller could not have mis-filed it.
        Assert.Equal(Domain.LearningEventType.EvidenceAccepted, appended.Value.Type);

        var stream = await store.ReadStreamAsync(learner);
        var rehydrated = EventCatalogue.Deserialize<EvidenceAccepted>(stream[0]);

        Assert.Equal(EvidenceType.Build, rehydrated.Type);
        Assert.Equal(0.95, rehydrated.Assurance);
    }

    [Fact]
    public async Task A_batch_of_typed_events_lands_as_one_session()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        // A whole session, as it actually happens: start, get taught, spend a hint, answer,
        // and have the answer adjudicated. Five facts, one transaction.
        var batch = new EventBatch(learner)
            .Add(new SessionStarted(session, PlannedMinutes: 15), now)
            .Add(new ConceptTaught(session, Source: "synthesised", Seconds: 210), now.AddMinutes(1), concept)
            .Add(new HintTaken(session, HintRung.Probe, BudgetRemaining: 2), now.AddMinutes(5), concept)
            .Add(new AnswerGiven(session, Guid.CreateVersion7(), EvidenceType.Apply, Correct: true, StatedConfidence: 0.6, Seconds: 120), now.AddMinutes(7), concept)
            .Add(new SessionCompleted(session, ActualMinutes: 14, EvidenceProduced: 1), now.AddMinutes(14))
            .Build();

        var result = await store.AppendAsync(learner, batch, IEventStore.EmptyStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Count);

        var stream = await store.ReadStreamAsync(learner);

        // Every one of them rehydrates into its own record type.
        Assert.All(stream, e => Assert.NotNull(EventCatalogue.Deserialize(e)));

        var hint = EventCatalogue.Deserialize<HintTaken>(stream[2]);
        Assert.Equal(HintRung.Probe, hint.Rung);
        Assert.Equal(2, hint.BudgetRemaining);
    }

    [Fact]
    public async Task Being_taught_something_is_recorded_but_is_not_evidence()
    {
        // The whole thesis, in one test. A learner can sit through instruction all day; the log
        // records it, and nothing about what they *know* has changed. Only EvidenceAccepted can
        // move mastery, and it is not in this stream.
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var session = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await store.AppendAsync(
            learner,
            new EventBatch(learner)
                .Add(new SessionStarted(session, 25), now)
                .Add(new ConceptTaught(session, "synthesised", 600), now.AddMinutes(1), concept)
                .Add(new ConceptTaught(session, "synthesised", 600), now.AddMinutes(11), concept)
                .Add(new SessionCompleted(session, 25, EvidenceProduced: 0), now.AddMinutes(25))
                .Build(),
            IEventStore.EmptyStream);

        var stream = await store.ReadStreamAsync(learner);

        Assert.Equal(4, stream.Count);
        Assert.DoesNotContain(stream, e => e.Type == Domain.LearningEventType.EvidenceAccepted);

        // And the session honestly records that nothing was produced.
        var completed = EventCatalogue.Deserialize<SessionCompleted>(stream[^1]);
        Assert.Equal(0, completed.EvidenceProduced);
    }
}
