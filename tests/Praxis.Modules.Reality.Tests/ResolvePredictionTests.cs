using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Features.Loops;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Tests;

/// **Reality settles the claim — and a broken daemon is never allowed to settle it wrongly.**
///
/// The sandbox is faked here so the *handler's* rules are the thing under test: it runs the frozen
/// subject, records the outcome, resolves each commitment exactly once, and — the load-bearing one —
/// treats a sandbox outage as a failure that leaves the loop open, never as "you were wrong".
public sealed class ResolvePredictionTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Resolving_runs_the_frozen_subject_and_records_the_outcome()
    {
        var loopId = Guid.CreateVersion7();
        var store = new FakeEventStore([Made(loopId, "Output", "print(4)", "4", 0.8)]);
        var sandbox = new FakeSandbox(Ran("4\n"));

        var result = await Handler(store, sandbox).HandleAsync(new ResolvePrediction.Command(loopId), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Correct);

        var resolved = Assert.IsType<PredictionResolved>(Assert.Single(store.Appended).Payload);
        Assert.Equal(loopId, resolved.LoopId);
        Assert.True(resolved.Correct);
    }

    [Fact]
    public async Task A_sandbox_outage_is_not_a_verdict_and_the_loop_stays_open()
    {
        var loopId = Guid.CreateVersion7();
        var store = new FakeEventStore([Made(loopId, "Output", "print(4)", "4", 0.9)]);
        var sandbox = new FakeSandbox(Result<ExecutionResult>.Invalid("Docker is down."));

        var result = await Handler(store, sandbox).HandleAsync(new ResolvePrediction.Command(loopId), default);

        Assert.False(result.IsSuccess);
        // The loop is untouched — a wrong resolution here would poison the calibration record forever.
        Assert.Empty(store.Appended);
    }

    [Fact]
    public async Task A_prediction_resolves_exactly_once()
    {
        var loopId = Guid.CreateVersion7();
        var store = new FakeEventStore(
        [
            Made(loopId, "Output", "print(4)", "4", 0.8),
            ResolvedEvent(loopId),
        ]);

        var result = await Handler(store, new FakeSandbox(Ran("4\n")))
            .HandleAsync(new ResolvePrediction.Command(loopId), default);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public async Task Resolving_an_unknown_loop_is_not_found()
    {
        var store = new FakeEventStore([]);

        var result = await Handler(store, new FakeSandbox(Ran("4\n")))
            .HandleAsync(new ResolvePrediction.Command(Guid.CreateVersion7()), default);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ResolvePrediction.Handler Handler(FakeEventStore store, FakeSandbox sandbox)
        => new(store, sandbox, new FakeUser(Guid.CreateVersion7()), new FixedClock(Day1));

    private static Result<ExecutionResult> Ran(string stdout)
        => Result<ExecutionResult>.Success(new ExecutionResult(ExecutionOutcome.Passed, 0, stdout, "", 5));

    private static LearningEvent Made(Guid loopId, string kind, string subject, string claim, double confidence)
        => Ev(LearningEventType.PredictionMade, new PredictionMade(loopId, claim, confidence, Day1, kind, subject));

    private static LearningEvent ResolvedEvent(Guid loopId)
        => Ev(LearningEventType.PredictionResolved, new PredictionResolved(loopId, true, 0.04, "4"));

    private static LearningEvent Ev(LearningEventType type, IEventPayload payload)
        => LearningEvent.Create(Guid.CreateVersion7(), type, EventCatalogue.Serialize(payload), Day1, Day1);

    private sealed class FakeSandbox(Result<ExecutionResult> outcome) : ISandbox
    {
        public Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
            => Task.FromResult(outcome);
    }

    private sealed class FakeEventStore(IReadOnlyList<LearningEvent> stream) : IEventStore
    {
        public List<NewEvent> Appended { get; } = [];

        public Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
            Guid learnerId, long fromSequenceExclusive = 0, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(stream);

        public Task<Result<LearningEvent>> AppendAsync(NewEvent newEvent, CancellationToken ct = default)
        {
            Appended.Add(newEvent);
            return Task.FromResult(Result<LearningEvent>.Success(
                LearningEvent.Create(newEvent.LearnerId, newEvent.Type, "{}", Day1, Day1)));
        }

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
