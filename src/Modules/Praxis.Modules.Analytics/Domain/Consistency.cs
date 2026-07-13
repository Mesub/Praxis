using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Analytics.Domain;

/// **Do you come back at a cadence that makes learning stick — or in bursts that fade?**
///
/// This is deliberately *not* a streak. "Kept a streak" is on the list of things PRAXIS refuses to
/// pay for, because a streak counter turns learning into a slot machine and punishes a rest day the
/// science says you needed. What actually matters for retention is **spacing**: a learner who returns
/// every couple of days holds far more than one who crams five sessions into a Sunday and vanishes for
/// three weeks. So consistency here measures the *regularity of return*, and says the honest thing —
/// steady practice is praised, cram-then-vanish is gently flagged — without ever shaming a gap.
///
/// ## How it stays honest
///
/// It folds the calendar dates of real engagement — `SessionCompleted` and `SessionAbandoned`, the
/// same events `MinutesInvested` counts, because a session you walked out of was still a day you
/// showed up. It looks only at the *shape* of the history the learner practiced, never at "how long
/// since you last came" — that clock-relative "you've lapsed" signal is the decay debt's job, not
/// this one. So, like every other fold in this engine, it needs no clock and a rebuild reproduces the
/// same answer.
///
/// Two refusals:
///
/// - **Too little to say.** Regularity is a statement about the *gaps between* active days; you need
///   at least three active days (two gaps) before there is a pattern rather than an anecdote. Below
///   that it returns `NotEnoughData`, said as a blank.
/// - **Regular is not "every day".** A steady every-three-days cadence is regular. The metric measures
///   evenness of return, not frequency, so it never pressures a learner toward daily grinding.
public static class Consistency
{
    /// Below three active days there are fewer than two gaps, and a single gap is an anecdote, not a
    /// cadence. Reporting a pattern from it would be the same overconfidence the calibration metric
    /// exists to catch.
    public const int MinimumActiveDaysToReport = 3;

    /// Regularity is `1 - CV` (coefficient of variation of the gaps). At or above this the return
    /// cadence is even enough to call *regular*; below it the gaps swing too wildly — bursts and
    /// silences — to be anything but *erratic*. A CV of ~0.5 is the line: purely random returns sit
    /// near CV 1.0 (regularity 0), perfectly even returns at CV 0 (regularity 1).
    public const double RegularityThreshold = 0.5;

    public static ConsistencyReport From(IReadOnlyList<LearningEvent> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Distinct calendar days on which the learner actually engaged, in order. Two sessions on one
        // day are one day of showing up — consistency is about days returned, not sessions logged.
        var activeDays = new SortedSet<DateOnly>();
        foreach (var e in stream)
        {
            if (e.Type is LearningEventType.SessionCompleted or LearningEventType.SessionAbandoned)
            {
                activeDays.Add(DateOnly.FromDateTime(e.OccurredAt));
            }
        }

        if (activeDays.Count < MinimumActiveDaysToReport)
        {
            return ConsistencyReport.None with { ActiveDays = activeDays.Count };
        }

        var days = activeDays.ToArray();
        var span = days[^1].DayNumber - days[0].DayNumber + 1;

        // The gaps between consecutive active days, in days. [d0, d2, d5] → [2, 3].
        var gaps = new double[days.Length - 1];
        for (var i = 1; i < days.Length; i++)
        {
            gaps[i - 1] = days[i].DayNumber - days[i - 1].DayNumber;
        }

        var meanGap = Mean(gaps);
        var cv = meanGap > 0 ? StdDev(gaps, meanGap) / meanGap : 0.0;
        var regularity = Math.Clamp(1.0 - cv, 0.0, 1.0);

        var pattern = regularity >= 1.0 - RegularityThreshold
            ? ConsistencyPattern.Regular
            : ConsistencyPattern.Erratic;

        return new ConsistencyReport(pattern, activeDays.Count, span, meanGap, regularity);
    }

    private static double Mean(double[] values)
    {
        var sum = 0.0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }

    private static double StdDev(double[] values, double mean)
    {
        var sumSq = 0.0;
        foreach (var v in values)
        {
            var d = v - mean;
            sumSq += d * d;
        }

        return Math.Sqrt(sumSq / values.Length);
    }
}

/// The shape of a learner's return cadence. `NotEnoughData` is a real answer: fewer than three active
/// days, so there is no pattern to describe yet.
public enum ConsistencyPattern
{
    NotEnoughData = 0,

    /// Returns at a steady cadence — the spacing that makes learning stick.
    Regular = 1,

    /// Bursts of activity with long silent gaps between them — cram-then-vanish.
    Erratic = 2,
}

/// The cadence, and the numbers behind it, so the screen can say *"about every N days"* without
/// recomputing. `MeanGapDays` and `Regularity` are null when there were too few active days to form a
/// pattern.
public sealed record ConsistencyReport(
    ConsistencyPattern Pattern,
    int ActiveDays,
    int SpanDays,
    double? MeanGapDays,
    double? Regularity)
{
    public static readonly ConsistencyReport None =
        new(ConsistencyPattern.NotEnoughData, 0, 0, null, null);

    public bool HasPattern => Pattern != ConsistencyPattern.NotEnoughData;
}
