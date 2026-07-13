using Praxis.Modules.Analytics.Domain;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Analytics.Tests;

/// **Is the learner getting better at knowing what they know?** The trend compares the earlier half of
/// a learner's graded history against the recent half — but only when there is enough on each side to
/// compare, and only calls a move a move when it clears the noise floor.
public sealed class CalibrationTrendTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    /// A graded item: the learner staked `confidence` and was `correct` or not. Perfectly-calibrated
    /// items (confidence 1.0 when right) score Brier 0; badly-calibrated ones (confidence 1.0 when
    /// wrong) score Brier 1.
    private static LearningEvent Answer(double confidence, bool correct)
        => LearningEvent.Create(
            Guid.CreateVersion7(),
            LearningEventType.AnswerGiven,
            EventCatalogue.Serialize(new AnswerGiven(
                Guid.CreateVersion7(), Guid.CreateVersion7(), EvidenceType.Apply, correct, confidence, 30)),
            Day1, Day1);

    private static LearningEvent Noise()
        => LearningEvent.Create(
            Guid.CreateVersion7(),
            LearningEventType.SessionCompleted,
            EventCatalogue.Serialize(new SessionCompleted(Guid.CreateVersion7(), 20, 1)),
            Day1, Day1);

    [Fact]
    public void Too_few_graded_items_is_not_enough_data_not_a_flat_line()
    {
        // Nine items — one short of two full windows. There is nothing honest to compare.
        var stream = Enumerable.Range(0, 9).Select(_ => Answer(0.5, true)).ToArray();

        var trend = CalibrationTrend.From(stream);

        Assert.Equal(CalibrationDirection.NotEnoughData, trend.Direction);
        Assert.False(trend.HasTrend);
        Assert.Null(trend.EarlierBrier);
        Assert.Null(trend.RecentBrier);
    }

    [Fact]
    public void An_empty_history_has_no_trend()
        => Assert.Equal(CalibrationDirection.NotEnoughData, CalibrationTrend.From([]).Direction);

    [Fact]
    public void Calibration_getting_better_reads_as_improving()
    {
        // Earlier half: confident and wrong (Brier 1.0). Recent half: confident and right (Brier 0.0).
        var stream = new List<LearningEvent>();
        for (var i = 0; i < 5; i++) stream.Add(Answer(1.0, correct: false));
        for (var i = 0; i < 5; i++) stream.Add(Answer(1.0, correct: true));

        var trend = CalibrationTrend.From(stream);

        Assert.Equal(CalibrationDirection.Improving, trend.Direction);
        Assert.Equal(1.0, trend.EarlierBrier!.Value, 3);
        Assert.Equal(0.0, trend.RecentBrier!.Value, 3);
        Assert.Equal(5, trend.EarlierSamples);
        Assert.Equal(5, trend.RecentSamples);
    }

    [Fact]
    public void Calibration_sliding_reads_as_declining()
    {
        // The dangerous drift: started calibrated, drifting toward confident-and-wrong.
        var stream = new List<LearningEvent>();
        for (var i = 0; i < 5; i++) stream.Add(Answer(1.0, correct: true));   // Brier 0.0
        for (var i = 0; i < 5; i++) stream.Add(Answer(1.0, correct: false));  // Brier 1.0

        Assert.Equal(CalibrationDirection.Declining, CalibrationTrend.From(stream).Direction);
    }

    [Fact]
    public void A_wobble_smaller_than_the_noise_floor_is_holding_steady()
    {
        // Both halves near the same Brier — a change under SignificantDelta must not be dressed up.
        var stream = new List<LearningEvent>();
        for (var i = 0; i < 6; i++) stream.Add(Answer(0.7, correct: true));   // Brier 0.09
        for (var i = 0; i < 6; i++) stream.Add(Answer(0.72, correct: true));  // Brier ~0.078

        var trend = CalibrationTrend.From(stream);

        Assert.Equal(CalibrationDirection.HoldingSteady, trend.Direction);
        Assert.True(trend.HasTrend);
    }

    [Fact]
    public void Only_graded_items_count_session_noise_is_ignored()
    {
        // Interleave non-graded events; they must not shift the split or the means.
        var stream = new List<LearningEvent>();
        for (var i = 0; i < 5; i++) { stream.Add(Noise()); stream.Add(Answer(1.0, correct: false)); }
        for (var i = 0; i < 5; i++) { stream.Add(Answer(1.0, correct: true)); stream.Add(Noise()); }

        var trend = CalibrationTrend.From(stream);

        Assert.Equal(CalibrationDirection.Improving, trend.Direction);
        Assert.Equal(5, trend.EarlierSamples);
        Assert.Equal(5, trend.RecentSamples);
    }
}
