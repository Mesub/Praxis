using Praxis.Modules.Kernel.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Events;

public static class EventStoreExtensions
{
    /// Append by payload. The event type comes from the payload itself, so it is not possible to
    /// file an <see cref="ExplanationRecorded"/> under <see cref="LearningEventType.EvidenceAccepted"/>
    /// — a mistake that would quietly grant mastery for having talked.
    public static Task<Result<LearningEvent>> AppendAsync<TPayload>(
        this IEventStore store,
        Guid learnerId,
        TPayload payload,
        DateTime occurredAt,
        Guid? conceptId = null,
        CancellationToken ct = default)
        where TPayload : IEventPayload
    {
        ArgumentNullException.ThrowIfNull(store);

        return store.AppendAsync(
            new NewEvent(learnerId, TPayload.EventType, payload!, occurredAt, conceptId),
            ct);
    }
}

/// Builds a batch for one learner, keeping the payload-to-type pairing honest.
public sealed class EventBatch
{
    private readonly List<NewEvent> _events = [];

    public Guid LearnerId { get; }

    public EventBatch(Guid learnerId) => LearnerId = learnerId;

    public EventBatch Add<TPayload>(TPayload payload, DateTime occurredAt, Guid? conceptId = null)
        where TPayload : IEventPayload
    {
        _events.Add(new NewEvent(LearnerId, TPayload.EventType, payload!, occurredAt, conceptId));
        return this;
    }

    public IReadOnlyList<NewEvent> Build() => _events;
}
