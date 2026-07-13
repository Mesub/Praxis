using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Features.Loops;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Tests;

/// **A commitment is frozen before the code runs, and it is refused if it cannot be scored.**
///
/// The API validator gives a clean 400, but the handler enforces the same rule itself — so the
/// invariant holds even if the two ever drift. A confidence outside 0..1 appends nothing: an
/// unscoreable forecast is not written to a log that is supposed to be about calibration.
public sealed class CommitPredictionTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_valid_commitment_appends_a_prediction_made_with_the_claim_and_confidence()
    {
        var store = new RecordingEventStore();
        var handler = new CommitPrediction.Handler(store, new FakeUser(Guid.CreateVersion7()), new FixedClock(Now));

        var result = await handler.HandleAsync(
            new CommitPrediction.Command(PredictionKind.Output, "print(2+2)", "4", 0.8), default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.LoopId);

        var made = Assert.IsType<PredictionMade>(Assert.Single(store.Appended).Payload);
        Assert.Equal("4", made.Claim);
        Assert.Equal(0.8, made.StatedConfidence);
    }

    [Fact]
    public async Task An_unscoreable_confidence_is_refused_and_nothing_is_written()
    {
        var store = new RecordingEventStore();
        var handler = new CommitPrediction.Handler(store, new FakeUser(Guid.CreateVersion7()), new FixedClock(Now));

        var result = await handler.HandleAsync(
            new CommitPrediction.Command(PredictionKind.Output, "print(1)", "1", 1.5), default);

        Assert.False(result.IsSuccess);
        Assert.Empty(store.Appended);
    }

    private sealed class RecordingEventStore : IEventStore
    {
        public List<NewEvent> Appended { get; } = [];

        public Task<Result<LearningEvent>> AppendAsync(NewEvent newEvent, CancellationToken ct = default)
        {
            Appended.Add(newEvent);
            return Task.FromResult(Result<LearningEvent>.Success(
                LearningEvent.Create(newEvent.LearnerId, newEvent.Type, "{}", Now, Now)));
        }

        public Task<Result<IReadOnlyList<LearningEvent>>> AppendAsync(
            Guid learnerId, IReadOnlyList<NewEvent> events, long? expectedLastSequence, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
            Guid learnerId, long fromSequenceExclusive = 0, int? limit = null, CancellationToken ct = default)
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
