namespace Praxis.Modules.Assessment.Domain;

/// **One thing a learner did, as Assessment remembers it.**
///
/// This is a projection table, not a source of truth: every row here was derived from an
/// `AnswerGiven` on the event log, and `ResetAsync` throws the lot away and rebuilds it.
///
/// ## Why Assessment keeps its own copy
///
/// The estimator needs *every* response a learner has ever given on a concept, because it recomputes
/// the posterior from the wide prior each time rather than nudging the last number (M7.2 — an
/// incremental update cannot be replayed, and a belief about a person's mind that depends on the
/// order the server happened to process things in is not a belief, it is an accident).
///
/// The obvious way to get them is to read `kernel.learning_events` directly. **No module reads
/// another module's tables** — so Assessment builds its own view of the history it cares about, from
/// the events the runner hands it, and queries that. The kernel stays the single writer of truth and
/// Assessment stays the single reader of its own schema.
///
/// ## And the confidence is here, stated *before* the outcome
///
/// `StatedConfidence` was captured before the learner found out whether they were right. That
/// ordering is the entire basis of calibration (C-6) — a confidence collected afterwards measures
/// nothing but hindsight, and would make the most valuable signal in the product worthless while
/// still producing a number.
public sealed class RecordedResponse
{
    public Guid Id { get; private set; }

    /// The event this row was derived from. **Unique** — if the runner ever redelivers, the database
    /// says no rather than letting a learner be scored twice for one answer.
    ///
    /// The event's *sequence* would be the obvious key, and it is the wrong one: the kernel assigns
    /// it on insert, so it does not exist until the event has been persisted, and a projection that
    /// keys on it cannot be exercised on an event that was never written. The `Id` is the event's
    /// identity from the moment it is created, which is what an idempotency key needs to be.
    public Guid EventId { get; private set; }

    public Guid LearnerId { get; private set; }

    public Guid ConceptId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid SessionId { get; private set; }

    public bool Correct { get; private set; }

    /// **Stated before the outcome was known.** See above; it is why calibration means anything.
    public double StatedConfidence { get; private set; }

    public int Seconds { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private RecordedResponse() { }

    internal static RecordedResponse From(
        Guid eventId,
        Guid learnerId,
        Guid conceptId,
        Guid sessionId,
        Guid itemId,
        bool correct,
        double statedConfidence,
        int seconds,
        DateTime occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            LearnerId = learnerId,
            ConceptId = conceptId,
            SessionId = sessionId,
            ItemId = itemId,
            Correct = correct,
            StatedConfidence = statedConfidence,
            Seconds = seconds,
            OccurredAt = occurredAt,
        };
}
