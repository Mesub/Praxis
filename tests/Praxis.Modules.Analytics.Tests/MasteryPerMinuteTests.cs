using Praxis.Modules.Analytics.Domain;

namespace Praxis.Modules.Analytics.Tests;

/// **The headline number, and the two lies it refuses to tell.** It will not invent a rate over no
/// time, and it will not hide a week where nothing was learned. Everything else is one division.
public sealed class MasteryPerMinuteTests
{
    [Fact]
    public void The_rate_is_mastery_over_minutes()
    {
        var mpm = MasteryPerMinute.Compute(masteryCreditGained: 3.0, minutesInvested: 120.0);

        Assert.Equal(0.025, mpm.Rate);
        Assert.True(mpm.HasData);
        Assert.False(mpm.GainedNothing);
    }

    [Fact]
    public void No_minutes_invested_is_undefined_not_zero_and_not_infinity()
    {
        var mpm = MasteryPerMinute.Compute(masteryCreditGained: 0.0, minutesInvested: 0.0);

        // There is nothing to divide yet — say so, rather than reporting 0 or ∞.
        Assert.Null(mpm.Rate);
        Assert.False(mpm.HasData);
        Assert.False(mpm.GainedNothing);
    }

    [Fact]
    public void Minutes_spent_with_no_mastery_gained_is_an_honest_zero()
    {
        var mpm = MasteryPerMinute.Compute(masteryCreditGained: 0.0, minutesInvested: 90.0);

        // A real, reportable bad week. PRAXIS will say this out loud (I-10).
        Assert.Equal(0.0, mpm.Rate);
        Assert.True(mpm.HasData);
        Assert.True(mpm.GainedNothing);
    }

    [Fact]
    public void A_loss_of_mastery_is_reported_as_zero_gained_never_a_negative_rate()
    {
        // Decay outran learning this window. The minutes bought nothing — not less than nothing.
        var mpm = MasteryPerMinute.Compute(masteryCreditGained: -1.5, minutesInvested: 60.0);

        Assert.Equal(0.0, mpm.Rate);
        Assert.True(mpm.GainedNothing);
        Assert.Equal(0.0, mpm.MasteryCreditGained);
    }

    [Fact]
    public void More_minutes_for_the_same_learning_lowers_the_rate()
    {
        // The anti-engagement property: keeping a learner on the app longer for the same mastery is a
        // *worse* MPM, and the number says so.
        var quick = MasteryPerMinute.Compute(2.0, 40.0);
        var padded = MasteryPerMinute.Compute(2.0, 80.0);

        Assert.True(padded.Rate < quick.Rate);
    }

    [Theory]
    [InlineData(0.0, 30.0)]
    [InlineData(5.0, 300.0)]
    [InlineData(12.5, 1_000.0)]
    public void The_rate_never_exceeds_the_gain_and_is_never_negative(double gain, double minutes)
    {
        var mpm = MasteryPerMinute.Compute(gain, minutes);

        Assert.NotNull(mpm.Rate);
        Assert.True(mpm.Rate >= 0.0);
        Assert.True(mpm.Rate <= gain);
    }
}
