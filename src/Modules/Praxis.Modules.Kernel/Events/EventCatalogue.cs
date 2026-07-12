using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Kernel.Events;

/// The closed set of things that can happen to a learner, and the payload shape of each.
///
/// Built by reflection over the payload records rather than by a hand-written switch, so that
/// adding an event type without a payload — or a payload without an event type — is caught by
/// <see cref="EveryEventTypeHasExactlyOnePayload"/> instead of by a learner.
public static class EventCatalogue
{
    private static readonly FrozenDictionary<LearningEventType, Type> ByEventType = Build();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<LearningEventType, Type> All => ByEventType;

    public static Type PayloadTypeFor(LearningEventType type)
        => ByEventType.TryGetValue(type, out var payload)
            ? payload
            : throw new InvalidOperationException(
                $"{type} has no payload record. Every event in the catalogue must have exactly one.");

    public static string Serialize(IEventPayload payload)
        => JsonSerializer.Serialize(payload, payload?.GetType() ?? typeof(object), Json);

    /// Rehydrate an event's payload into its record. Projections call this; they never parse JSON.
    public static IEventPayload Deserialize(LearningEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var target = PayloadTypeFor(e.Type);

        return JsonSerializer.Deserialize(e.Payload, target, Json) as IEventPayload
            ?? throw new InvalidOperationException(
                $"Event {e.Sequence} ({e.Type}) has a payload that is not a valid {target.Name}. "
                + "The log is append-only, so this cannot be repaired by editing it — the writer "
                + "that produced it was wrong.");
    }

    public static TPayload Deserialize<TPayload>(LearningEvent e)
        where TPayload : IEventPayload
        => (TPayload)Deserialize(e);

    /// Throws if the catalogue is inconsistent. Called by the startup check and by the tests.
    public static void EveryEventTypeHasExactlyOnePayload()
    {
        var declared = Enum.GetValues<LearningEventType>();

        var missing = declared.Where(t => !ByEventType.ContainsKey(t)).ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "These event types have no payload record: "
                + string.Join(", ", missing)
                + ". An event nothing can read is an event that will be written and then silently "
                + "ignored by every projection.");
        }
    }

    private static FrozenDictionary<LearningEventType, Type> Build()
    {
        var payloads = typeof(EventCatalogue).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && t.IsAssignableTo(typeof(IEventPayload)))
            .ToList();

        var map = new Dictionary<LearningEventType, Type>();

        foreach (var payload in payloads)
        {
            var eventType = (LearningEventType)payload
                .GetProperty(nameof(IEventPayload.EventType), BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;

            if (map.TryGetValue(eventType, out var already))
            {
                throw new InvalidOperationException(
                    $"{already.Name} and {payload.Name} both claim to be {eventType}. "
                    + "An event has exactly one shape, or projections cannot trust what they read.");
            }

            map[eventType] = payload;
        }

        return map.ToFrozenDictionary();
    }
}
