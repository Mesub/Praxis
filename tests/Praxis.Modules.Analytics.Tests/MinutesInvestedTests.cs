using Praxis.Modules.Analytics.Domain;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;

namespace Praxis.Modules.Analytics.Tests;

/// **The denominator counts the time you spent, not the time that worked.** Completed and abandoned
/// sessions both count; what you merely *planned* does not.
public sealed class MinutesInvestedTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LearningEvent Ev(LearningEventType type, IEventPayload payload)
        => LearningEvent.Create(Guid.CreateVersion7(), type, EventCatalogue.Serialize(payload), Day1, Day1);

    [Fact]
    public void Completed_and_abandoned_minutes_both_count()
    {
        var stream = new[]
        {
            Ev(LearningEventType.SessionCompleted, new SessionCompleted(Guid.CreateVersion7(), 15, 1)),
            Ev(LearningEventType.SessionAbandoned, new SessionAbandoned(Guid.CreateVersion7(), 5, "session")),
            Ev(LearningEventType.SessionCompleted, new SessionCompleted(Guid.CreateVersion7(), 25, 2)),
        };

        // 15 + 5 (abandoned time is still time) + 25.
        Assert.Equal(45.0, MinutesInvested.From(stream));
    }

    [Fact]
    public void Planned_minutes_are_not_counted_you_are_measured_on_what_you_did()
    {
        var stream = new[]
        {
            Ev(LearningEventType.SessionStarted, new SessionStarted(Guid.CreateVersion7(), 45)),
        };

        Assert.Equal(0.0, MinutesInvested.From(stream));
    }

    [Fact]
    public void An_empty_history_is_zero_minutes()
        => Assert.Equal(0.0, MinutesInvested.From([]));
}
