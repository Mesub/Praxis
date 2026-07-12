using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Persistence;

public sealed class EventStore : IEventStore
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly KernelDbContext _db;
    private readonly IClock _clock;

    public EventStore(KernelDbContext db, IClock clock) => (_db, _clock) = (db, clock);

    public async Task<Result<LearningEvent>> AppendAsync(NewEvent newEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newEvent);

        var result = await AppendAsync(newEvent.LearnerId, [newEvent], expectedLastSequence: null, ct);

        return result.IsSuccess
            ? Result<LearningEvent>.Success(result.Value[0])
            : Result<LearningEvent>.Invalid(result.Message ?? "Append failed.", result.Errors);
    }

    public async Task<Result<IReadOnlyList<LearningEvent>>> AppendAsync(
        Guid learnerId,
        IReadOnlyList<NewEvent> events,
        long? expectedLastSequence,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return Result<IReadOnlyList<LearningEvent>>.Invalid("Nothing to append.");
        }

        if (events.Any(e => e.LearnerId != learnerId))
        {
            // A batch is one learner's history moving forward. Mixing learners would make the
            // optimistic check meaningless and the transaction boundary a lie.
            return Result<IReadOnlyList<LearningEvent>>.Invalid(
                "Every event in a batch must belong to the same learner.");
        }

        if (expectedLastSequence is { } expected)
        {
            var actual = await LastSequenceAsync(learnerId, ct);

            if (actual != expected)
            {
                return Result<IReadOnlyList<LearningEvent>>.Conflict(
                    $"This learner's stream has moved on. Expected to append after sequence "
                    + $"{expected}, but it now ends at {actual}. Re-read and decide again — "
                    + "do not overwrite.");
            }
        }

        var now = _clock.UtcNow;

        var appended = events
            .Select(e => LearningEvent.Create(
                e.LearnerId,
                e.Type,
                Serialize(e.Payload),
                e.OccurredAt,
                now,
                e.ConceptId))
            .ToList();

        _db.Events.AddRange(appended);

        await _db.SaveChangesAsync(ct);

        return Result<IReadOnlyList<LearningEvent>>.Success(appended);
    }

    public async Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
        Guid learnerId,
        long fromSequenceExclusive = 0,
        int? limit = null,
        CancellationToken ct = default)
    {
        var query = _db.Events
            .AsNoTracking()
            .Where(e => e.LearnerId == learnerId && e.Sequence > fromSequenceExclusive)
            .OrderBy(e => e.Sequence);

        if (limit is { } take)
        {
            return await query.Take(take).ToListAsync(ct);
        }

        return await query.ToListAsync(ct);
    }

    public async IAsyncEnumerable<LearningEvent> ReadAllFromAsync(
        long fromSequenceExclusive = 0,
        int batchSize = 500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var cursor = fromSequenceExclusive;

        while (!ct.IsCancellationRequested)
        {
            // Keyset pagination on the sequence, not OFFSET. A rebuild walks the entire log, and
            // OFFSET would make it quadratic.
            var batch = await _db.Events
                .AsNoTracking()
                .Where(e => e.Sequence > cursor)
                .OrderBy(e => e.Sequence)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                yield break;
            }

            foreach (var e in batch)
            {
                yield return e;
            }

            cursor = batch[^1].Sequence;
        }
    }

    public async Task<long> LastSequenceAsync(Guid learnerId, CancellationToken ct = default)
        => await _db.Events
            .AsNoTracking()
            .Where(e => e.LearnerId == learnerId)
            .MaxAsync(e => (long?)e.Sequence, ct)
            ?? IEventStore.EmptyStream;

    private static string Serialize(object payload)
        => payload as string ?? JsonSerializer.Serialize(payload, PayloadJson);
}
