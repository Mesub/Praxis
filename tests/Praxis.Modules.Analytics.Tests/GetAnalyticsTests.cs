using Praxis.Modules.Analytics.Features;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Analytics.Tests;

/// **The number, assembled: minutes from the log over mastery from the Twin.** The handler does no
/// arithmetic of its own beyond calling the pure `MasteryPerMinute` — it only gathers the two inputs.
public sealed class GetAnalyticsTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task It_divides_verified_mastery_by_minutes_invested()
    {
        var store = new FakeEventStore(
        [
            Event(new SessionCompleted(Guid.CreateVersion7(), 40, 2)),
            Event(new SessionCompleted(Guid.CreateVersion7(), 20, 1)),
        ]);

        var result = await new GetAnalytics.Handler(store, new FakeSnapshot(3.0), new FakeUser())
            .HandleAsync(new GetAnalytics.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.0, result.Value.MasteryCredit);
        Assert.Equal(60.0, result.Value.MinutesInvested);
        Assert.Equal(0.05, result.Value.Rate);
        Assert.True(result.Value.HasData);
    }

    [Fact]
    public async Task A_learner_who_has_invested_nothing_gets_no_rate_not_a_zero()
    {
        var result = await new GetAnalytics.Handler(new FakeEventStore([]), new FakeSnapshot(0.0), new FakeUser())
            .HandleAsync(new GetAnalytics.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Rate);
        Assert.False(result.Value.HasData);
    }

    private static LearningEvent Event(IEventPayload payload)
        => LearningEvent.Create(
            Guid.CreateVersion7(), LearningEventType.SessionCompleted, EventCatalogue.Serialize(payload), Day1, Day1);

    private sealed class FakeSnapshot(double mastery) : IAnalyticsSnapshot
    {
        public Task<double> MasteryCreditByAsync(Guid learnerId, CancellationToken ct = default)
            => Task.FromResult(mastery);
    }

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

    private sealed class FakeUser : IUserContext
    {
        public Guid? LearnerId { get; } = Guid.CreateVersion7();

        public bool IsAuthenticated => true;

        public Guid RequireLearnerId() => LearnerId!.Value;
    }
}
