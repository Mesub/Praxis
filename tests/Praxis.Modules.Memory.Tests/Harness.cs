using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Memory.Tests;

/// **The oracle, stubbed — and the stub is bound by the same contract the real one is.**
///
/// `CheckClaimResponse` has three words and an optional key, and **nowhere to put an answer**. So even
/// a deliberately malicious fake, written by somebody actively trying to leak the answer into the
/// core, could not do it. That is what it means for a guarantee to be structural rather than a matter
/// of good behaviour: **it holds against the test doubles too.**
public sealed class FakeOracle : IAiPlane
{
    private ClaimOutcome _outcome = ClaimOutcome.Correct;
    private bool _unreachable;

    public void Says(ClaimOutcome outcome)
    {
        _outcome = outcome;
        _unreachable = false;
    }

    /// The oracle is down. **PRAXIS must not guess, and must not default to WRONG.**
    public void Fails() => _unreachable = true;

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
        => Task.FromResult(_unreachable
            ? Result<CheckClaimResponse>.Invalid("The oracle could not be reached.")
            : Result<CheckClaimResponse>.Success(new CheckClaimResponse(_outcome, null, 0.0004)));

    public Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

    public Task<Result<ItemResponse>> GenerateItemAsync(GenerateItemRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

    public Task<Result<TutorTurnResponse>> TutorTurnAsync(TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

    public IAsyncEnumerable<string> TutorTurnStreamAsync(TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");
}

/// Captures what was appended, so a test can assert **that the rep went on the log** rather than
/// taking the handler's word for it.
public sealed class RecordingEventStore : IEventStore
{
    public List<object> Appended { get; } = [];

    public Task<Result<LearningEvent>> AppendAsync(NewEvent newEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newEvent);

        Appended.Add(newEvent.Payload);

        return Task.FromResult(Result<LearningEvent>.Success(LearningEvent.Create(
            newEvent.LearnerId,
            newEvent.Type,
            EventCatalogue.Serialize((IEventPayload)newEvent.Payload),
            newEvent.OccurredAt,
            newEvent.OccurredAt,
            newEvent.ConceptId)));
    }

    public Task<Result<IReadOnlyList<LearningEvent>>> AppendAsync(
        Guid learnerId,
        IReadOnlyList<NewEvent> events,
        long? expectedLastSequence,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var landed = new List<LearningEvent>();

        foreach (var e in events)
        {
            landed.Add(AppendAsync(e, ct).Result.Value);
        }

        return Task.FromResult(Result<IReadOnlyList<LearningEvent>>.Success(landed));
    }

    public Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
        Guid learnerId, long fromSequenceExclusive = 0, int? limit = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LearningEvent>>([]);

    public async IAsyncEnumerable<LearningEvent> ReadAllFromAsync(
        long fromSequenceExclusive = 0,
        int batchSize = 500,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;

        yield break;
    }

    public Task<long> LastSequenceAsync(Guid learnerId, CancellationToken ct = default)
        => Task.FromResult(IEventStore.EmptyStream);
}

/// A runner with **no projections registered**.
///
/// `CatchUpAsync` iterates its projection list and never touches the database when that list is
/// empty — so this is honest rather than a stub that pretends. These tests are about the *handler*:
/// what it appends, what it refuses, and what it tells the learner. The projections have their own
/// tests, against a real Postgres.
public static class NoProjections
{
    public static ProjectionRunner Runner()
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=never-connected;Username=x;Password=y")
            .Options;

        return new ProjectionRunner(
            new RecordingEventStore(),
            new KernelDbContext(options),
            new FixedClock(),
            NullLogger<ProjectionRunner>.Instance,
            []);
    }
}

public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
}

public sealed class FakeUser : IUserContext
{
    public FakeUser(Guid learnerId) => LearnerId = learnerId;

    public Guid? LearnerId { get; }

    public bool IsAuthenticated => true;

    public Guid RequireLearnerId() => LearnerId!.Value;
}
