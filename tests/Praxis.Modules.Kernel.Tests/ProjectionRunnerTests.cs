using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Tests;

/// A stand-in for the Twin: it counts accepted evidence per concept, in memory.
///
/// Crude, but it has the property that matters — it holds a *belief about a learner* that exists
/// nowhere in the log, and can only be arrived at by replaying history.
internal sealed class EvidenceCountProjection : IProjection
{
    public EvidenceCountProjection(string name) => Name = name;

    /// Each test gets its own name, and therefore its own checkpoint row. Sharing one would make
    /// these tests order-dependent: whoever ran first would leave a checkpoint that told the next
    /// projection it had already read history it has never seen.
    public string Name { get; }

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.EvidenceAccepted,
    };

    public Dictionary<Guid, int> AcceptedPerConcept { get; } = [];

    /// Counts every event handed to it — so if the runner ever replays an event twice, the
    /// numbers drift and the test notices.
    public int TimesApplied { get; private set; }

    /// What the runner actually handed over. Lets a test assert the filter held, without
    /// depending on how much history happens to be in the shared database.
    public HashSet<LearningEventType> SeenTypes { get; } = [];

    public Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        TimesApplied++;
        SeenTypes.Add(e.Type);

        if (e.ConceptId is { } concept)
        {
            AcceptedPerConcept[concept] = AcceptedPerConcept.GetValueOrDefault(concept) + 1;
        }

        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        AcceptedPerConcept.Clear();
        SeenTypes.Clear();
        TimesApplied = 0;
        return Task.CompletedTask;
    }
}

[Collection(SharedPostgres.Name)]
public sealed class ProjectionRunnerTests
{
    private readonly PostgresFixture _postgres;

    public ProjectionRunnerTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task A_projection_sees_the_events_it_handles_and_no_others()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(
            learner,
            new EventBatch(learner)
                .Add(new SessionStarted(session, 15), now)
                .Add(new ConceptTaught(session, "synthesised", 200), now, concept)
                .Add(new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 1.0, 0.9), now, concept)
                .Add(new SessionCompleted(session, 15, 1), now)
                .Build(),
            IEventStore.EmptyStream);

        var (runner, projection) = Runner(db, store);

        await runner.CatchUpAsync();

        // Of this learner's four events, exactly one was evidence — and that is all the
        // projection was handed. It never saw the session, and it never saw the teaching.
        Assert.Equal(1, projection.AcceptedPerConcept[concept]);
        Assert.Equal([LearningEventType.EvidenceAccepted], projection.SeenTypes);
    }

    [Fact]
    public async Task Catching_up_twice_does_not_apply_the_same_event_twice()
    {
        // The checkpoint's whole job. Without it, every run would re-count all of history and
        // the Twin would inflate a little more each time it ran.
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(
            learner,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 0.95),
            DateTime.UtcNow,
            concept);

        var (runner, projection) = Runner(db, store);

        await runner.CatchUpAsync();
        var second = await runner.CatchUpAsync();

        // Nothing new arrived, so nothing was applied — and the count did not creep.
        Assert.Equal(0, second.EventsApplied);
        Assert.Equal(1, projection.AcceptedPerConcept[concept]);
    }

    [Fact]
    public async Task A_projection_picks_up_only_what_arrived_since_it_last_looked()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(
            learner, new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 1.0, 0.9),
            DateTime.UtcNow, concept);

        var (runner, projection) = Runner(db, store);
        await runner.CatchUpAsync();

        await store.AppendAsync(
            learner, new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Predict, 1.0, 1.0),
            DateTime.UtcNow, concept);

        var result = await runner.CatchUpAsync();

        Assert.Equal(1, result.EventsApplied);
        Assert.Equal(2, projection.AcceptedPerConcept[concept]);
    }

    [Fact]
    public async Task The_checkpoint_advances_past_events_the_projection_ignores()
    {
        // Otherwise the projection re-reads the entire tail of the log on every single run,
        // forever — it would look correct and get slower every day.
        var learner = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var taughtConcept = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        // Nothing here is EvidenceAccepted. The projection handles none of it. Note the learner
        // was taught the concept — and, as the whole product insists, that is not evidence.
        await store.AppendAsync(
            learner,
            new EventBatch(learner)
                .Add(new SessionStarted(session, 15), now)
                .Add(new ConceptTaught(session, "synthesised", 200), now, taughtConcept)
                .Add(new SessionAbandoned(session, 3, "teach-back"), now)
                .Build(),
            IEventStore.EmptyStream);

        var (runner, projection) = Runner(db, store);
        await runner.CatchUpAsync();

        var checkpoint = db.Checkpoints.Single(c => c.ProjectionName == projection.Name);

        // The checkpoint has moved past this learner's events even though the projection applied
        // none of them. Without that, it would re-read this tail on every run, forever.
        Assert.True(
            checkpoint.LastSequence >= await store.LastSequenceAsync(learner),
            "The checkpoint did not advance past events the projection ignores.");

        // And being taught the concept produced nothing. The projection holds no belief about it,
        // because the learner never proved anything — which is the entire thesis, in one assert.
        Assert.DoesNotContain(taughtConcept, projection.AcceptedPerConcept.Keys);
    }

    [Fact]
    public async Task Rebuilding_from_zero_reproduces_the_state_exactly()
    {
        // The claim the entire architecture rests on: everything PRAXIS believes about a learner
        // can be thrown away and recomputed from history alone. If this ever fails, some belief
        // is being held somewhere that history cannot audit — and the Ledger's claims stop being
        // verifiable.
        var learner = Guid.CreateVersion7();
        var conceptA = Guid.CreateVersion7();
        var conceptB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var batch = new EventBatch(learner);
        for (var i = 0; i < 7; i++)
        {
            batch.Add(new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 1.0, 0.9),
                now.AddMinutes(i), conceptA);
        }
        for (var i = 0; i < 3; i++)
        {
            batch.Add(new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 0.95),
                now.AddMinutes(10 + i), conceptB);
        }

        await store.AppendAsync(learner, batch.Build(), IEventStore.EmptyStream);

        var (runner, projection) = Runner(db, store);

        await runner.CatchUpAsync();

        var before = new Dictionary<Guid, int>(projection.AcceptedPerConcept);
        var appliedBefore = projection.TimesApplied;

        Assert.Equal(7, before[conceptA]);
        Assert.Equal(3, before[conceptB]);

        await runner.RebuildAsync();

        // Byte for byte, the same beliefs — arrived at a second time, from history alone.
        Assert.Equal(before, projection.AcceptedPerConcept);

        // And arrived at exactly once. If the reset had not actually reset, this would double.
        Assert.Equal(appliedBefore, projection.TimesApplied);
    }

    [Fact]
    public async Task A_rebuild_destroys_what_the_projection_held_before_replaying()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var (runner, projection) = Runner(db, store);

        // A belief with no basis in the log. A rebuild must not preserve it.
        projection.AcceptedPerConcept[concept] = 999;

        await store.AppendAsync(
            learner, new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 1.0, 0.9),
            DateTime.UtcNow, concept);

        await runner.RebuildAsync();

        Assert.Equal(1, projection.AcceptedPerConcept[concept]);
    }

    [Fact]
    public void Two_projections_cannot_share_a_name()
    {
        // The name IS the checkpoint key. Two projections sharing one would overwrite each
        // other's place in history, and both would silently skip events.
        using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var ex = Assert.Throws<InvalidOperationException>(() => new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance,
            [new EvidenceCountProjection("twin"), new EvidenceCountProjection("twin")]));

        Assert.Contains("checkpoint key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_checkpoint_cannot_move_backwards()
    {
        var checkpoint = ProjectionCheckpoint.StartOf("test", DateTime.UtcNow);
        checkpoint.AdvanceTo(100, DateTime.UtcNow);

        var ex = Assert.Throws<InvalidOperationException>(
            () => checkpoint.AdvanceTo(50, DateTime.UtcNow));

        Assert.Contains("backwards", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (ProjectionRunner Runner, EvidenceCountProjection Projection) Runner(
        KernelDbContext db,
        EventStore store,
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var projection = new EvidenceCountProjection($"test.evidence-count.{name}");

        var runner = new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance, [projection]);

        return (runner, projection);
    }
}
