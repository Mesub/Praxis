using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Tests;

/// A projection that writes to the database, so the rebuild has something real to destroy.
///
/// It is a crude stand-in for the Twin (M3): per concept, how much accepted evidence, weighted by
/// assurance. Nothing here is stored in the log — it is a *belief*, arrived at only by replaying
/// history. Which is exactly what makes it worth testing.
internal sealed class BeliefProjection : IProjection
{
    private readonly KernelDbContext _db;

    public BeliefProjection(KernelDbContext db, string name)
    {
        _db = db;
        Name = name;
    }

    public string Name { get; }

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.EvidenceAccepted,
    };

    public static async Task EnsureTableAsync(KernelDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS kernel.test_beliefs (
                projection   text   NOT NULL,
                learner_id   uuid   NOT NULL,
                concept_id   uuid   NOT NULL,
                evidence     int    NOT NULL,
                belief       double precision NOT NULL,
                PRIMARY KEY (projection, learner_id, concept_id)
            );
            """);
    }

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ConceptId is not { } concept)
        {
            return;
        }

        var payload = EventCatalogue.Deserialize<EvidenceAccepted>(e);

        // Belief accumulates by how much the evidence can be trusted. An EXPLAIN graded by a
        // model counts for less than a BUILD that actually ran — the shape the real Twin's
        // Evidence Ceiling (C-4) will enforce properly in M3.5.
        var contribution = payload.Score * payload.Assurance;

        await _db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO kernel.test_beliefs (projection, learner_id, concept_id, evidence, belief)
             VALUES ({Name}, {e.LearnerId}, {concept}, 1, {contribution})
             ON CONFLICT (projection, learner_id, concept_id) DO UPDATE
             SET evidence = kernel.test_beliefs.evidence + 1,
                 belief   = kernel.test_beliefs.belief + {contribution}
             """, ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
        => await _db.Database.ExecuteSqlAsync(
            $"DELETE FROM kernel.test_beliefs WHERE projection = {Name}", ct);

    /// The state, in a form two runs can be compared byte for byte.
    public async Task<string> SnapshotAsync()
    {
        var rows = await _db.Database
            .SqlQuery<BeliefRow>($"""
                SELECT learner_id AS "LearnerId", concept_id AS "ConceptId",
                       evidence AS "Evidence", belief AS "Belief"
                FROM kernel.test_beliefs
                WHERE projection = {Name}
                ORDER BY learner_id, concept_id
                """)
            .ToListAsync();

        return JsonSerializer.Serialize(rows);
    }

    internal sealed record BeliefRow(Guid LearnerId, Guid ConceptId, int Evidence, double Belief);
}

/// **If this test is ever red, the kernel is broken and nothing else matters.**
///
/// The claim: everything PRAXIS believes about a learner can be destroyed and recomputed from the
/// event log alone. If it cannot, then some belief is being kept somewhere history cannot audit —
/// and the moment that is true, the Twin is fiction and the Ledger's claims stop being verifiable.
[Collection(SharedPostgres.Name)]
public sealed class RebuildFromZeroTests
{
    private readonly PostgresFixture _postgres;

    public RebuildFromZeroTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Fifty_events_rebuilt_from_zero_produce_byte_identical_state()
    {
        await using var db = _postgres.CreateContext();
        await BeliefProjection.EnsureTableAsync(db);

        var store = new EventStore(db, new SystemClock());
        var projection = new BeliefProjection(db, $"rebuild.{Guid.CreateVersion7():N}");
        var runner = new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance, [projection]);

        var learner = Guid.CreateVersion7();
        await AppendFiftyEvents(store, learner);

        await runner.CatchUpAsync();
        var before = await projection.SnapshotAsync();

        Assert.NotEqual("[]", before);   // it actually believes something

        // Destroy everything it holds, and recompute it from history.
        await runner.RebuildAsync();
        var after = await projection.SnapshotAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_rebuild_erases_a_belief_that_history_does_not_support()
    {
        // The sharp end of it. Someone — a bug, a bad migration, a person with psql — writes a
        // belief into the Twin that no evidence justifies. A rebuild must remove it, because the
        // log is the only thing that gets to decide what is true.
        await using var db = _postgres.CreateContext();
        await BeliefProjection.EnsureTableAsync(db);

        var store = new EventStore(db, new SystemClock());
        var name = $"forged.{Guid.CreateVersion7():N}";
        var projection = new BeliefProjection(db, name);
        var runner = new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance, [projection]);

        var learner = Guid.CreateVersion7();
        await AppendFiftyEvents(store, learner);

        await runner.CatchUpAsync();
        var honest = await projection.SnapshotAsync();

        // A concept this learner has never produced a single piece of evidence for, asserted as
        // mastered.
        var forgedConcept = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO kernel.test_beliefs (projection, learner_id, concept_id, evidence, belief)
             VALUES ({name}, {learner}, {forgedConcept}, 99, 99.0)
             """);

        Assert.Contains(forgedConcept.ToString(), await projection.SnapshotAsync(), StringComparison.Ordinal);

        await runner.RebuildAsync();

        var rebuilt = await projection.SnapshotAsync();

        Assert.DoesNotContain(forgedConcept.ToString(), rebuilt, StringComparison.Ordinal);
        Assert.Equal(honest, rebuilt);
    }

    [Fact]
    public async Task Rebuilding_twice_is_the_same_as_rebuilding_once()
    {
        // Replay must be idempotent. If a second rebuild drifts, then the projection is reading
        // something other than the log — a clock, a random, a network call — and its output is
        // no longer a function of history.
        await using var db = _postgres.CreateContext();
        await BeliefProjection.EnsureTableAsync(db);

        var store = new EventStore(db, new SystemClock());
        var projection = new BeliefProjection(db, $"twice.{Guid.CreateVersion7():N}");
        var runner = new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance, [projection]);

        await AppendFiftyEvents(store, Guid.CreateVersion7());

        await runner.RebuildAsync();
        var once = await projection.SnapshotAsync();

        await runner.RebuildAsync();
        var twice = await projection.SnapshotAsync();

        Assert.Equal(once, twice);
    }

    [Fact]
    public async Task The_log_still_holds_all_fifty_events_after_a_rebuild()
    {
        // A rebuild destroys *derived* state. It must never touch the source.
        await using var db = _postgres.CreateContext();
        await BeliefProjection.EnsureTableAsync(db);

        var store = new EventStore(db, new SystemClock());
        var projection = new BeliefProjection(db, $"intact.{Guid.CreateVersion7():N}");
        var runner = new ProjectionRunner(
            store, db, new SystemClock(), NullLogger<ProjectionRunner>.Instance, [projection]);

        var learner = Guid.CreateVersion7();
        await AppendFiftyEvents(store, learner);

        await runner.RebuildAsync();

        var stream = await store.ReadStreamAsync(learner);

        Assert.Equal(50, stream.Count);
    }

    /// A learner's fortnight: sessions, teaching, hints, wrong answers, misconceptions, and — only
    /// sometimes — evidence that actually counts.
    private static async Task AppendFiftyEvents(EventStore store, Guid learner)
    {
        var concepts = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        var start = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

        var batch = new EventBatch(learner);
        var appended = 0;

        for (var day = 0; appended < 50; day++)
        {
            var session = Guid.CreateVersion7();
            var at = start.AddDays(day);
            var concept = concepts[day % concepts.Length];

            void Add<T>(T payload, int minutes, Guid? c = null) where T : IEventPayload
            {
                if (appended >= 50)
                {
                    return;
                }

                batch.Add(payload, at.AddMinutes(minutes), c);
                appended++;
            }

            Add(new SessionStarted(session, 25), 0);
            Add(new ConceptTaught(session, "synthesised", 240), 1, concept);
            Add(new HintTaken(session, HintRung.Probe, 2), 6, concept);

            Add(new AnswerGiven(session, Guid.CreateVersion7(), EvidenceType.Apply,
                Correct: day % 3 != 0, StatedConfidence: 0.5 + (day % 4 * 0.1), Seconds: 90), 8, concept);

            if (day % 3 == 0)
            {
                // Got it wrong. That is not a failure of the system; it is the system working.
                Add(new MisconceptionDetected(Guid.CreateVersion7(),
                    "confuses recall with precision", "answered R when P was asked"), 9, concept);
                Add(new EvidenceRejected(Guid.CreateVersion7(), EvidenceType.Apply, "wrong"), 10, concept);
            }
            else
            {
                // Produced something. Only this moves belief.
                Add(new EvidenceAccepted(Guid.CreateVersion7(),
                    day % 2 == 0 ? EvidenceType.Build : EvidenceType.Apply,
                    Score: 1.0,
                    Assurance: day % 2 == 0 ? 0.95 : 0.80), 10, concept);
            }

            Add(new SessionCompleted(session, 25, 1), 25);
        }

        var result = await store.AppendAsync(learner, batch.Build(), IEventStore.EmptyStream);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(50, result.Value.Count);
    }
}
