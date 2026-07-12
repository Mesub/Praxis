using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Tests;

[Collection(SharedPostgres.Name)]
public sealed class EventStoreTests
{
    private readonly PostgresFixture _postgres;

    public EventStoreTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task Appending_returns_the_event_with_the_sequence_the_database_assigned()
    {
        var learner = Guid.CreateVersion7();
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var result = await store.AppendAsync(new NewEvent(
            learner,
            LearningEventType.AnswerGiven,
            new { itemId = "rag-01", correct = true, confidence = 0.8 },
            DateTime.UtcNow));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Sequence > 0);
        Assert.Contains("rag-01", result.Value.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_payload_object_is_serialised_and_comes_back_intact()
    {
        var learner = Guid.CreateVersion7();
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(new NewEvent(
            learner,
            LearningEventType.PredictionMade,
            new { claim = "this loop is O(n^2)", confidence = 0.65 },
            DateTime.UtcNow));

        var stream = await store.ReadStreamAsync(learner);

        Assert.Single(stream);
        Assert.Contains("O(n^2)", stream[0].Payload, StringComparison.Ordinal);
        Assert.Contains("0.65", stream[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_reads_back_in_append_order()
    {
        var learner = Guid.CreateVersion7();
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        foreach (var i in Enumerable.Range(0, 6))
        {
            await store.AppendAsync(new NewEvent(
                learner, LearningEventType.RepAnswered, new { n = i }, DateTime.UtcNow));
        }

        var stream = await store.ReadStreamAsync(learner);

        Assert.Equal(6, stream.Count);
        Assert.Equal(
            stream.Select(e => e.Sequence).OrderBy(s => s),
            stream.Select(e => e.Sequence));
    }

    [Fact]
    public async Task A_stream_can_be_read_from_a_point_which_is_how_a_projection_catches_up()
    {
        var learner = Guid.CreateVersion7();
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var first = await store.AppendAsync(new NewEvent(
            learner, LearningEventType.SessionStarted, new { }, DateTime.UtcNow));

        await store.AppendAsync(new NewEvent(
            learner, LearningEventType.AnswerGiven, new { correct = true }, DateTime.UtcNow));

        var since = await store.ReadStreamAsync(learner, fromSequenceExclusive: first.Value.Sequence);

        Assert.Single(since);
        Assert.Equal(LearningEventType.AnswerGiven, since[0].Type);
    }

    [Fact]
    public async Task One_learners_stream_never_contains_another_learners_events()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(new NewEvent(alice, LearningEventType.AnswerGiven, new { who = "alice" }, DateTime.UtcNow));
        await store.AppendAsync(new NewEvent(bob, LearningEventType.AnswerGiven, new { who = "bob" }, DateTime.UtcNow));

        var aliceStream = await store.ReadStreamAsync(alice);

        Assert.Single(aliceStream);
        Assert.Contains("alice", aliceStream[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_lands_atomically()
    {
        var learner = Guid.CreateVersion7();
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var result = await store.AppendAsync(
            learner,
            [
                new NewEvent(learner, LearningEventType.SessionStarted, new { }, DateTime.UtcNow),
                new NewEvent(learner, LearningEventType.AnswerGiven, new { correct = false }, DateTime.UtcNow),
                new NewEvent(learner, LearningEventType.MisconceptionDetected, new { name = "confuses recall with precision" }, DateTime.UtcNow),
            ],
            expectedLastSequence: IEventStore.EmptyStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var stream = await store.ReadStreamAsync(learner);
        Assert.Equal(3, stream.Count);
    }

    [Fact]
    public async Task A_batch_mixing_learners_is_refused()
    {
        var learner = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var result = await store.AppendAsync(
            learner,
            [
                new NewEvent(learner, LearningEventType.AnswerGiven, new { }, DateTime.UtcNow),
                new NewEvent(someoneElse, LearningEventType.AnswerGiven, new { }, DateTime.UtcNow),
            ],
            expectedLastSequence: null);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Appending_on_a_stale_view_of_the_stream_is_refused()
    {
        // Two writers both read the stream, both decide, both try to append. The second one is
        // deciding on history that no longer exists, and must be told so rather than quietly
        // appending a conclusion drawn from a stale Twin.
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var first = await store.AppendAsync(new NewEvent(
            learner, LearningEventType.AnswerGiven, new { correct = true }, DateTime.UtcNow));

        var staleView = first.Value.Sequence;

        // Someone else appends in between.
        await store.AppendAsync(new NewEvent(
            learner, LearningEventType.HintTaken, new { rung = "probe" }, DateTime.UtcNow));

        var result = await store.AppendAsync(
            learner,
            [new NewEvent(learner, LearningEventType.EvidenceAccepted, new { }, DateTime.UtcNow)],
            expectedLastSequence: staleView);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("moved on", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Asserting_an_empty_stream_fails_once_the_learner_has_history()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        await store.AppendAsync(new NewEvent(
            learner, LearningEventType.SessionStarted, new { }, DateTime.UtcNow));

        var result = await store.AppendAsync(
            learner,
            [new NewEvent(learner, LearningEventType.SessionStarted, new { }, DateTime.UtcNow)],
            expectedLastSequence: IEventStore.EmptyStream);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task The_last_sequence_of_an_untouched_learner_is_the_empty_stream()
    {
        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var last = await store.LastSequenceAsync(Guid.CreateVersion7());

        Assert.Equal(IEventStore.EmptyStream, last);
    }

    [Fact]
    public async Task The_whole_log_can_be_walked_in_batches_which_is_how_a_rebuild_works()
    {
        var learner = Guid.CreateVersion7();

        await using var db = _postgres.CreateContext();
        var store = new EventStore(db, new SystemClock());

        var seeded = await store.AppendAsync(
            learner,
            [.. Enumerable.Range(0, 25).Select(i =>
                new NewEvent(learner, LearningEventType.RepAnswered, new { n = i }, DateTime.UtcNow))],
            expectedLastSequence: IEventStore.EmptyStream);

        var from = seeded.Value[0].Sequence - 1;

        var walked = new List<LearningEvent>();

        // A deliberately small batch size, so the pagination actually pages.
        await foreach (var e in store.ReadAllFromAsync(from, batchSize: 4))
        {
            walked.Add(e);
        }

        Assert.Equal(25, walked.Count);
        Assert.Equal(
            walked.Select(e => e.Sequence).OrderBy(s => s),
            walked.Select(e => e.Sequence));
        Assert.Equal(walked.Select(e => e.Sequence).Distinct().Count(), walked.Count);
    }
}
