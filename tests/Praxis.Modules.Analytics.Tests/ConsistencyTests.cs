using Praxis.Modules.Analytics.Domain;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;

namespace Praxis.Modules.Analytics.Tests;

/// **Do you come back at a cadence that makes learning stick — or in bursts that fade?** Consistency
/// measures the *regularity* of return across distinct active days, never a streak, and says nothing
/// until there is a real pattern to describe.
public sealed class ConsistencyTests
{
    private static readonly DateTime Anchor = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    /// A completed session on a given day offset from the anchor.
    private static LearningEvent OnDay(int dayOffset)
        => LearningEvent.Create(
            Guid.CreateVersion7(),
            LearningEventType.SessionCompleted,
            EventCatalogue.Serialize(new SessionCompleted(Guid.CreateVersion7(), 20, 1)),
            Anchor.AddDays(dayOffset), Anchor.AddDays(dayOffset));

    private static LearningEvent AbandonedOnDay(int dayOffset)
        => LearningEvent.Create(
            Guid.CreateVersion7(),
            LearningEventType.SessionAbandoned,
            EventCatalogue.Serialize(new SessionAbandoned(Guid.CreateVersion7(), 5, "session")),
            Anchor.AddDays(dayOffset), Anchor.AddDays(dayOffset));

    [Fact]
    public void Fewer_than_three_active_days_is_not_enough_to_call_a_pattern()
    {
        var trend = Consistency.From([OnDay(0), OnDay(2)]);

        Assert.Equal(ConsistencyPattern.NotEnoughData, trend.Pattern);
        Assert.False(trend.HasPattern);
        Assert.Equal(2, trend.ActiveDays);
        Assert.Null(trend.MeanGapDays);
    }

    [Fact]
    public void An_empty_history_has_no_pattern()
        => Assert.Equal(ConsistencyPattern.NotEnoughData, Consistency.From([]).Pattern);

    [Fact]
    public void A_steady_cadence_reads_as_regular()
    {
        // Every two days, evenly — the spacing that makes learning stick.
        var trend = Consistency.From([OnDay(0), OnDay(2), OnDay(4), OnDay(6)]);

        Assert.Equal(ConsistencyPattern.Regular, trend.Pattern);
        Assert.Equal(4, trend.ActiveDays);
        Assert.Equal(2.0, trend.MeanGapDays!.Value, 3);
        Assert.Equal(1.0, trend.Regularity!.Value, 3);   // perfectly even → regularity 1
    }

    [Fact]
    public void Cram_then_vanish_reads_as_erratic()
    {
        // Three days in a row, then a three-week silence, then one more: bursty gaps.
        var trend = Consistency.From([OnDay(0), OnDay(1), OnDay(2), OnDay(23)]);

        Assert.Equal(ConsistencyPattern.Erratic, trend.Pattern);
        Assert.Equal(4, trend.ActiveDays);
    }

    [Fact]
    public void Two_sessions_on_one_day_are_one_day_of_showing_up()
    {
        // Four events but only three distinct days — consistency counts days returned, not sessions.
        var trend = Consistency.From([OnDay(0), OnDay(0), OnDay(2), OnDay(4)]);

        Assert.Equal(3, trend.ActiveDays);
        Assert.Equal(ConsistencyPattern.Regular, trend.Pattern);
    }

    [Fact]
    public void Abandoned_days_still_count_as_showing_up()
    {
        // A day you walked out of was still a day you came — same rule as minutes invested.
        var trend = Consistency.From([OnDay(0), AbandonedOnDay(2), OnDay(4)]);

        Assert.Equal(3, trend.ActiveDays);
        Assert.True(trend.HasPattern);
    }

    [Fact]
    public void The_span_is_first_active_day_to_last_inclusive()
    {
        var trend = Consistency.From([OnDay(0), OnDay(2), OnDay(6)]);

        Assert.Equal(7, trend.SpanDays);   // day 0 through day 6 inclusive
    }
}
