using Praxis.Modules.Kernel.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Kernel;

/// The only way in or out of the event log.
///
/// Engines never touch `kernel.learning_events` directly. They append facts here and read
/// streams back; what they *believe* about a learner is derived, never stored (CANON §5.1).
public interface IEventStore
{
    /// Append one event. Returns the appended event, carrying the sequence the database assigned.
    Task<Result<LearningEvent>> AppendAsync(
        NewEvent newEvent,
        CancellationToken ct = default);

    /// Append several events as one transaction — all of them land, or none do.
    ///
    /// <paramref name="expectedLastSequence"/> is optimistic concurrency: pass the last sequence
    /// you saw for this learner and the append is refused with Conflict if their stream has moved
    /// since. Pass null when you genuinely do not care (most appends). Pass <see cref="EmptyStream"/>
    /// to assert the learner has no events at all.
    Task<Result<IReadOnlyList<LearningEvent>>> AppendAsync(
        Guid learnerId,
        IReadOnlyList<NewEvent> events,
        long? expectedLastSequence,
        CancellationToken ct = default);

    /// One learner's history, in order. This is what rebuilding a Twin reads.
    Task<IReadOnlyList<LearningEvent>> ReadStreamAsync(
        Guid learnerId,
        long fromSequenceExclusive = 0,
        int? limit = null,
        CancellationToken ct = default);

    /// The whole log, in order, in batches. This is what a projection rebuild reads.
    IAsyncEnumerable<LearningEvent> ReadAllFromAsync(
        long fromSequenceExclusive = 0,
        int batchSize = 500,
        CancellationToken ct = default);

    /// The last sequence in a learner's stream, or <see cref="EmptyStream"/> if they have none.
    Task<long> LastSequenceAsync(Guid learnerId, CancellationToken ct = default);

    /// The expected-last-sequence to pass when asserting a learner has no history yet.
    /// Sequences are database identities starting at 1, so 0 can never collide with a real one.
    public const long EmptyStream = 0;
}

/// An event on its way in — before the database has given it a place in the order.
public sealed record NewEvent(
    Guid LearnerId,
    LearningEventType Type,
    object Payload,
    DateTime OccurredAt,
    Guid? ConceptId = null);
