using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;

namespace Praxis.Modules.Analytics.Domain;

/// **Is the learner getting better at knowing what they know?**
///
/// The Twin reports a learner's *current* calibration — one Brier score, how well their stated
/// confidence matches their real correctness (lower is better). This asks the next question: is that
/// number moving, and which way? A learner whose calibration is improving is learning the most
/// valuable meta-skill there is — knowing the edge of their own competence. One whose calibration is
/// sliding is drifting toward the dangerous place: confident and wrong.
///
/// ## How it stays honest
///
/// It folds the same immutable `AnswerGiven` events the Twin's calibration is built from — every
/// graded item where the learner staked a confidence *before* seeing the outcome — and compares the
/// mean Brier of the **earlier half** of that history against the **recent half**. Two refusals keep
/// it from lying:
///
/// - **Not enough to compare, no trend.** A trend needs a meaningful sample on *each* side of the
///   split (`MinimumSamplesPerWindow` each). Below that, PRAXIS says "not enough yet" rather than
///   drawing a line through noise — the same discipline the Twin applies to a single Brier.
/// - **A wobble is not a trend.** A change in mean Brier smaller than `SignificantDelta` is reported
///   as *holding steady*, not dressed up as improvement or decline. The metric exists to catch a real
///   drift, not to manufacture a story out of sampling jitter.
///
/// It reads the log in order, so it is a pure fold and needs no clock: append order *is* time order
/// (the store hands events back by `Sequence`), which is what lets a rebuild reproduce the same trend.
public static class CalibrationTrend
{
    /// Below this many samples *in each window*, a trend is noise. Mirrors the Twin's rule that
    /// calibration under five samples is not knowledge — applied here to each side of the split.
    public const int MinimumSamplesPerWindow = 5;

    /// A shift in mean Brier smaller than this is sampling jitter, not a direction. Brier runs [0, 1];
    /// well-calibrated sits near 0 and the uninformative middle at 0.25, so 0.05 is a real move.
    public const double SignificantDelta = 0.05;

    public static CalibrationTrendReport From(IReadOnlyList<LearningEvent> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // One Brier per graded item, in the order they happened. Only AnswerGiven carries a
        // confidence staked before the outcome — the one thing that makes calibration measurable —
        // and it is the same surface the Twin's overall Brier is built from, so this is the trend of
        // the very number the screen already shows.
        var briers = new List<double>();
        foreach (var e in stream)
        {
            if (e.Type != LearningEventType.AnswerGiven)
            {
                continue;
            }

            var answer = EventCatalogue.Deserialize<AnswerGiven>(e);
            briers.Add(Brier(answer.StatedConfidence, answer.Correct));
        }

        // A trend is a comparison; it needs a real sample on both sides. If either window would be
        // thin, there is no trend to report — only a blank, said as a blank.
        if (briers.Count < 2 * MinimumSamplesPerWindow)
        {
            return CalibrationTrendReport.None;
        }

        var mid = briers.Count / 2;
        var earlier = Mean(briers, 0, mid);
        var recent = Mean(briers, mid, briers.Count);

        // Brier is a loss: a *lower* recent score means calibration improved.
        var delta = recent - earlier;
        var direction =
            delta < -SignificantDelta ? CalibrationDirection.Improving
            : delta > SignificantDelta ? CalibrationDirection.Declining
            : CalibrationDirection.HoldingSteady;

        return new CalibrationTrendReport(direction, earlier, recent, mid, briers.Count - mid);
    }

    /// The Brier score for one prediction: the squared gap between confidence and outcome. Inlined
    /// rather than borrowed from the Twin — Analytics folds the shared log on its own and does not
    /// reach into another engine.
    private static double Brier(double statedConfidence, bool correct)
    {
        var confidence = Math.Clamp(statedConfidence, 0.0, 1.0);
        var outcome = correct ? 1.0 : 0.0;
        return (confidence - outcome) * (confidence - outcome);
    }

    private static double Mean(List<double> values, int startInclusive, int endExclusive)
    {
        var sum = 0.0;
        for (var i = startInclusive; i < endExclusive; i++)
        {
            sum += values[i];
        }

        return sum / (endExclusive - startInclusive);
    }
}

/// Which way a learner's calibration is moving. `NotEnoughData` is a first-class answer, not a zero:
/// it means PRAXIS has not seen enough graded items to compare, and says so.
public enum CalibrationDirection
{
    NotEnoughData = 0,
    Improving = 1,
    HoldingSteady = 2,
    Declining = 3,
}

/// The trend, and the two means it is made of, so the screen can show *why* without recomputing:
/// a Brier that fell from `EarlierBrier` to `RecentBrier` is calibration getting better. Both are
/// null when there is not enough history to compare.
public sealed record CalibrationTrendReport(
    CalibrationDirection Direction,
    double? EarlierBrier,
    double? RecentBrier,
    int EarlierSamples,
    int RecentSamples)
{
    public static readonly CalibrationTrendReport None =
        new(CalibrationDirection.NotEnoughData, null, null, 0, 0);

    public bool HasTrend => Direction != CalibrationDirection.NotEnoughData;
}
