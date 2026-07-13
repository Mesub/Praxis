using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Features.Loops;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Tests;

/// **The board is a projection of the log — proved by folding real events into it.**
///
/// A prediction opens a loop; its resolution closes it. The commitment cannot be taken back, so the
/// board cannot be edited to make an uncomfortable prediction disappear — there is nothing to edit,
/// only immutable events to fold.
public sealed class GetLoopsBoardTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LearningEvent Made(Guid loopId, string claim, double confidence, DateTime resolvesAt)
        => Event(LearningEventType.PredictionMade, new PredictionMade(loopId, claim, confidence, resolvesAt));

    private static LearningEvent Resolved(Guid loopId, bool correct, double brier)
        => Event(LearningEventType.PredictionResolved, new PredictionResolved(loopId, correct, brier, "42"));

    private static LearningEvent Event(LearningEventType type, IEventPayload payload)
        => LearningEvent.Create(
            Guid.CreateVersion7(), type, EventCatalogue.Serialize(payload), Day1, Day1);

    [Fact]
    public void A_prediction_opens_a_loop_that_stands_until_reality_closes_it()
    {
        var loopId = Guid.CreateVersion7();

        var loops = GetLoopsBoard.Handler.Fold([Made(loopId, "prints 42", 0.8, Day1.AddDays(2))]);

        var loop = Assert.Single(loops);
        Assert.Equal(LoopStatus.Open, loop.Status);
        Assert.Equal("prints 42", loop.Claim);
        Assert.Equal(0.8, loop.Confidence);
        Assert.Null(loop.Correct);
    }

    [Fact]
    public void A_resolution_closes_the_matching_loop_with_the_outcome_and_the_brier()
    {
        var loopId = Guid.CreateVersion7();

        var loops = GetLoopsBoard.Handler.Fold(
            [Made(loopId, "raises KeyError", 0.9, Day1.AddDays(1)), Resolved(loopId, correct: false, brier: 0.81)]);

        var loop = Assert.Single(loops);
        Assert.Equal(LoopStatus.Resolved, loop.Status);
        Assert.False(loop.Correct);
        Assert.Equal(0.81, loop.BrierScore);
    }

    [Fact]
    public void A_resolution_with_no_commitment_is_ignored_not_invented()
    {
        var loops = GetLoopsBoard.Handler.Fold([Resolved(Guid.CreateVersion7(), correct: true, brier: 0.01)]);

        Assert.Empty(loops);
    }

    [Fact]
    public async Task The_handler_reads_the_stream_and_builds_the_board()
    {
        var learner = Guid.CreateVersion7();
        var open = Guid.CreateVersion7();
        var closed = Guid.CreateVersion7();

        var store = new FakeEventStore(
        [
            Made(open, "prints 42", 0.7, Day1.AddDays(3)),
            Made(closed, "raises ValueError", 0.6, Day1.AddDays(1)),
            Resolved(closed, correct: true, brier: 0.16),
        ]);

        var handler = new GetLoopsBoard.Handler(store, new FakeUser(learner), new FixedClock(Day1));

        var result = await handler.HandleAsync(new GetLoopsBoard.Query(), default);

        Assert.True(result.IsSuccess);
        // The open loop is on the board; the closed one is counted, not listed as open.
        Assert.Single(result.Value.Open);
        Assert.Equal(1, result.Value.Resolved);
        Assert.Equal("prints 42", result.Value.Open[0].Claim);
        // Coverage: one resolved, none forfeited.
        Assert.Equal(1.0, result.Value.Coverage);
    }

    // ── Minimal fakes ────────────────────────────────────────────────────────

    private sealed class FakeEventStore(IReadOnlyList<LearningEvent> stream) : IEventStore
    {
        public Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
            Guid learnerId, long fromSequenceExclusive = 0, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(stream);

        public Task<Result<LearningEvent>> AppendAsync(NewEvent newEvent, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<LearningEvent>>> AppendAsync(
            Guid learnerId, IReadOnlyList<NewEvent> events, long? expectedLastSequence, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<LearningEvent> ReadAllFromAsync(
            long fromSequenceExclusive = 0, int batchSize = 500, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<long> LastSequenceAsync(Guid learnerId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeUser(Guid learner) : IUserContext
    {
        public Guid? LearnerId => learner;

        public bool IsAuthenticated => true;

        public Guid RequireLearnerId() => learner;
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }
}
