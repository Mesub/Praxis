using Microsoft.EntityFrameworkCore;
using Npgsql;
using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Kernel.Tests;

/// The kernel's one promise: **the log grows, and it never changes.**
///
/// Everything PRAXIS says about a learner — what they know, what has decayed, what they can
/// prove to a stranger — is a projection of this table. If history can be edited, the Twin is
/// fiction and the Ledger is a rumour. So these tests try to edit it, by every route available:
/// through the ORM, and around the ORM with raw SQL.
[Collection(SharedPostgres.Name)]
public sealed class AppendOnlyTests
{
    private readonly PostgresFixture _postgres;

    public AppendOnlyTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task An_event_can_be_appended_and_read_back()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();
        var occurred = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);

        await using (var db = _postgres.CreateContext())
        {
            db.Events.Add(LearningEvent.Create(
                learner,
                LearningEventType.AnswerGiven,
                """{"itemId":"abc","correct":true}""",
                occurred,
                occurred.AddSeconds(2),
                concept));

            await db.SaveChangesAsync();
        }

        await using (var db = _postgres.CreateContext())
        {
            var stored = await db.Events.SingleAsync(e => e.LearnerId == learner);

            Assert.True(stored.Sequence > 0, "The database must assign the append order.");
            Assert.Equal(LearningEventType.AnswerGiven, stored.Type);
            Assert.Equal(concept, stored.ConceptId);
            Assert.Equal(occurred, stored.OccurredAt);
            Assert.Contains("\"correct\": true", stored.Payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_sequence_is_assigned_by_the_database_and_strictly_increases()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();

        for (var i = 0; i < 5; i++)
        {
            db.Events.Add(Event(learner, LearningEventType.RepAnswered, $$"""{"n":{{i}}}"""));
        }

        await db.SaveChangesAsync();

        var sequences = await db.Events
            .Where(e => e.LearnerId == learner)
            .OrderBy(e => e.Sequence)
            .Select(e => e.Sequence)
            .ToListAsync();

        Assert.Equal(5, sequences.Count);
        Assert.Equal(sequences.OrderBy(s => s), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
    }

    [Fact]
    public async Task Updating_an_event_through_the_orm_is_refused()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();

        var e = Event(learner, LearningEventType.ExplanationRecorded, """{"score":0.4}""");
        db.Events.Add(e);
        await db.SaveChangesAsync();

        // Try to improve a past grade. This is exactly the attack the kernel exists to refuse.
        db.Entry(e).Property(x => x.Payload).CurrentValue = """{"score":0.99}""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_an_event_through_the_orm_is_refused()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();

        var e = Event(learner, LearningEventType.SessionAbandoned, """{"reason":"walked away"}""");
        db.Events.Add(e);
        await db.SaveChangesAsync();

        // Delete the evidence of quitting. Also refused.
        db.Events.Remove(e);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Updating_an_event_with_raw_SQL_is_refused_by_the_database_itself()
    {
        // The ORM guard is a courtesy to whoever writes the bug. THIS is the guarantee: someone
        // with a psql prompt and good intentions still cannot rewrite a learner's history.
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        db.Events.Add(Event(learner, LearningEventType.AnswerGiven, """{"correct":false}"""));
        await db.SaveChangesAsync();

        var forged = """{"correct":true}""";

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE kernel.learning_events SET "Payload" = CAST({forged} AS jsonb) WHERE "LearnerId" = {learner}"""));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_with_raw_SQL_is_refused_by_the_database_itself()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        db.Events.Add(Event(learner, LearningEventType.HintTaken, """{"rung":"partial"}"""));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlAsync(
                $"""DELETE FROM kernel.learning_events WHERE "LearnerId" = {learner}"""));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Truncating_the_log_is_refused_by_the_database_itself()
    {
        await using var db = _postgres.CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlRawAsync("TRUNCATE kernel.learning_events"));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failed_append_leaves_no_trace()
    {
        // A rejected mutation must not take a legitimate append down with it, and must not
        // half-commit. The log is either exactly what it was, or exactly one event longer.
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();

        var good = Event(learner, LearningEventType.ConceptTaught, """{"ok":true}""");
        db.Events.Add(good);
        await db.SaveChangesAsync();

        db.Entry(good).Property(x => x.Payload).CurrentValue = """{"tampered":true}""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await using var fresh = _postgres.CreateContext();
        var stored = await fresh.Events.SingleAsync(e => e.LearnerId == learner);

        Assert.Contains("\"ok\": true", stored.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("tampered", stored.Payload, StringComparison.Ordinal);
    }

    private static LearningEvent Event(Guid learner, LearningEventType type, string payload)
        => LearningEvent.Create(learner, type, payload, DateTime.UtcNow, DateTime.UtcNow);
}
