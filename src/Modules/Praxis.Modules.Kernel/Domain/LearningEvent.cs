namespace Praxis.Modules.Kernel.Domain;

/// One immutable fact about something a learner did.
///
/// This is the only thing PRAXIS actually stores about learning. Everything else — what we
/// believe you know, what has decayed, what you can prove — is a *projection* of this log,
/// rebuildable from zero. See CANON §5.1.
///
/// Deliberately NOT a BaseEntity. An event has no UpdatedAt, because it is never updated; and
/// no IsDeleted, because it is never deleted. Inheriting those fields would imply otherwise,
/// and the whole trustworthiness of the Ledger rests on them not existing.
public sealed class LearningEvent
{
    /// Global append order. Assigned by the database, not by us — it is the log's spine, and
    /// two concurrent appends must never be able to agree on the same number.
    public long Sequence { get; private set; }

    public Guid Id { get; private set; }

    public Guid LearnerId { get; private set; }

    public LearningEventType Type { get; private set; }

    /// Most events are about a concept. Some — session lifecycle — are not.
    public Guid? ConceptId { get; private set; }

    /// The event's body, as JSONB. Shape varies by Type; each type's payload is a record in
    /// Events/, serialised on append and deserialised by whichever projection cares.
    public string Payload { get; private set; } = "{}";

    /// When it happened in the learner's world — not when we wrote the row. Decay is computed
    /// from this, so a delayed write must not make a concept look fresher than it is.
    public DateTime OccurredAt { get; private set; }

    /// When we durably recorded it. Differs from OccurredAt for offline/queued writes.
    public DateTime RecordedAt { get; private set; }

    private LearningEvent() { }   // EF

    public static LearningEvent Create(
        Guid learnerId,
        LearningEventType type,
        string payload,
        DateTime occurredAt,
        DateTime recordedAt,
        Guid? conceptId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (learnerId == Guid.Empty)
        {
            throw new ArgumentException("An event must belong to a learner.", nameof(learnerId));
        }

        if (occurredAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAt must be UTC.", nameof(occurredAt));
        }

        return new LearningEvent
        {
            Id = Guid.CreateVersion7(),
            LearnerId = learnerId,
            Type = type,
            ConceptId = conceptId,
            Payload = payload,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
        };
    }
}
