using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **The reliability curve — the actionable half of calibration a mean Brier throws away.**
///
/// A single Brier number tells a learner they are noisy; it cannot tell them *which direction*. These
/// tests build the curve from resolved predictions and check that it reads over- and under-confidence
/// the right way round, tells an abstainer from a miscalibrated learner, and refuses to draw a verdict
/// on thin data.
public sealed class CalibrationProfileTests
{
    /// `count` forecasts made at `confidence`, of which `hits` were correct.
    private static IEnumerable<Forecast> At(double confidence, int count, int hits)
        => Enumerable.Range(0, count).Select(i => new Forecast(confidence, i < hits));

    // ── Direction is the whole point ─────────────────────────────────────────

    /// **Well calibrated: the curve lies on the diagonal.** At every confidence level, they are right
    /// about that often. This is rarer than being good at the material, and worth as much.
    [Fact]
    public void A_learner_on_the_diagonal_is_well_calibrated()
    {
        var profile = Calibration.Build([
            .. At(0.9, 10, 9),   // said 90%, right 9/10
            .. At(0.5, 10, 5),   // said 50%, right 5/10
            .. At(0.1, 10, 1),   // said 10%, right 1/10
        ]);

        Assert.True(profile.HasVerdict);
        Assert.False(profile.IsHedging);
        Assert.True(Math.Abs(profile.Overconfidence) < 0.05);

        // Low reliability (on the diagonal) and real resolution (the buckets separate).
        Assert.True(profile.Reliability < 0.02);
        Assert.True(profile.Resolution > 0.05);

        Assert.Contains("well calibrated", profile.Headline, StringComparison.Ordinal);
    }

    /// **Overconfident: sure, and wrong more than they think.** The dangerous direction — they *act*
    /// on the certainty. Positive `Overconfidence`.
    [Fact]
    public void Saying_ninety_and_being_right_sixty_reads_as_overconfident()
    {
        var profile = Calibration.Build([.. At(0.9, 20, 12)]);   // said 90%, right 60%

        Assert.True(profile.Overconfidence > 0.2);
        Assert.Contains("overconfident", profile.Headline, StringComparison.Ordinal);

        // And the bucket at 0.9 sits well below the diagonal.
        var top = profile.Buckets.Single(b => b.LowerBound == 0.9);
        Assert.True(top.Gap < -0.2, "The 90% bucket did not register as below the diagonal.");
    }

    /// **Underconfident: they know more than they will claim.** Not dangerous, but it costs them.
    /// Negative `Overconfidence`.
    [Fact]
    public void Saying_sixty_and_being_right_ninety_reads_as_underconfident()
    {
        var profile = Calibration.Build([.. At(0.6, 20, 18)]);   // said 60%, right 90%

        Assert.True(profile.Overconfidence < -0.2);
        Assert.Contains("underconfident", profile.Headline, StringComparison.Ordinal);
    }

    // ── The abstainer is not miscalibrated ───────────────────────────────────

    /// **"50% about everything" is hedging, not miscalibration.**
    ///
    /// It scores a mediocre Brier of 0.25 — but the learner is not *wrong* about their confidence,
    /// they are refusing to commit. Their forecasts carry no information (near-zero resolution), and
    /// PRAXIS names that rather than coaching a calibration problem they do not have.
    [Fact]
    public void Someone_who_says_fifty_about_everything_is_hedging_not_miscalibrated()
    {
        var profile = Calibration.Build([.. At(0.5, 20, 10)]);

        // The tell: confidence pinned near 0.5, barely moving.
        Assert.True(profile.ConfidenceSpread < Calibration.HedgingSpread);
        Assert.True(profile.IsHedging);

        // A Brier that on its own looks like ordinary mediocrity.
        Assert.Equal(0.25, profile.Brier, tolerance: 1e-9);

        Assert.Contains("hedging", profile.Headline, StringComparison.Ordinal);

        // **And crucially, not praised as "well calibrated"** — which a naive overconfidence-only
        // check (their over/under is ~0) would have done.
        Assert.DoesNotContain("well calibrated", profile.Headline, StringComparison.Ordinal);
    }

    /// **A learner who says the same *confident* number about everything is not a hedger** — they are
    /// overconfident, the opposite problem. Both have zero resolution (one populated bucket), which is
    /// exactly why hedging is detected by *where* the confidence sits, not by resolution. This test is
    /// the bug that ambush caught: an all-0.9 learner briefly read as "hedging" until the detector was
    /// fixed.
    [Fact]
    public void Always_saying_ninety_is_overconfidence_not_hedging()
    {
        var profile = Calibration.Build([.. At(0.9, 20, 12)]);

        Assert.False(profile.IsHedging, "A confidently overconfident learner was mistaken for a hedger.");
        Assert.Contains("overconfident", profile.Headline, StringComparison.Ordinal);
    }

    // ── It will not diagnose on thin data ────────────────────────────────────

    /// **Below the floor, PRAXIS draws the curve but names no verdict** — a tool that calls someone
    /// overconfident on a handful of points has performed the very overconfidence it is measuring
    /// (M7.3).
    [Fact]
    public void On_thin_data_it_reports_no_verdict()
    {
        var profile = Calibration.Build([.. At(0.9, 3, 1)]);   // wildly overconfident, but n=3

        Assert.False(profile.HasVerdict);
        Assert.False(profile.IsHedging);   // cannot even call it hedging on this little
        Assert.Contains("Not enough resolved predictions", profile.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void The_verdict_floor_is_high_enough_to_mean_something()
    {
        Assert.True(Calibration.MinimumForVerdict >= 8);
    }

    // ── The Brier decomposition holds together ───────────────────────────────

    /// **Resolution rewards separating the wins from the losses**, independent of the absolute levels.
    /// A learner who is uniformly overconfident but *orders* their predictions well still has high
    /// resolution — it is a different, and recoverable, problem from hedging.
    [Fact]
    public void Resolution_is_high_when_forecasts_separate_outcomes_even_if_levels_are_off()
    {
        // Overconfident everywhere, but the high-confidence bucket really is more correct than the
        // low one: the forecasts carry information.
        var profile = Calibration.Build([
            .. At(0.95, 20, 16),   // 80% right
            .. At(0.55, 20, 6),    // 30% right
        ]);

        Assert.True(profile.Overconfidence > 0.15, "Should read as overconfident.");
        Assert.True(profile.Resolution > 0.05, "Forecasts separate outcomes; resolution should be real.");
        Assert.False(profile.IsHedging);
    }

    /// The Brier here is the same number the Twin computes (M3.4) — one definition of the score,
    /// wherever it lives.
    [Theory]
    [InlineData(1.0, true, 0.0)]
    [InlineData(1.0, false, 1.0)]
    [InlineData(0.5, true, 0.25)]
    [InlineData(0.8, true, 0.04)]
    public void The_Brier_matches_the_canonical_definition(double confidence, bool correct, double expected)
    {
        Assert.Equal(expected, Calibration.Brier(confidence, correct), tolerance: 1e-9);
    }

    // ── Buckets ──────────────────────────────────────────────────────────────

    /// Every prediction lands in exactly one bucket, and the counts add up to the sample size.
    [Fact]
    public void Every_prediction_lands_in_exactly_one_bucket()
    {
        var profile = Calibration.Build([
            .. At(0.0, 3, 0),
            .. At(0.35, 5, 2),
            .. At(1.0, 4, 4),    // the closed top edge must be caught
        ]);

        Assert.Equal(12, profile.Buckets.Sum(b => b.Count));
        Assert.Equal(profile.Samples, profile.Buckets.Sum(b => b.Count));

        // A confidence of exactly 1.0 belongs to the top bucket, not off the end.
        Assert.Equal(4, profile.Buckets.Single(b => b.UpperBound == 1.0).Count);
    }

    [Fact]
    public void An_empty_history_is_empty_and_says_nothing()
    {
        var profile = Calibration.Build([]);

        Assert.Equal(0, profile.Samples);
        Assert.False(profile.HasVerdict);
    }

    // ── Built straight from Reality-Loop resolutions ─────────────────────────

    /// The profile is built from the same `PredictionResolution` values M9.3/M9.3b produce — the
    /// Reality Loop feeding its own scoreboard.
    [Fact]
    public void It_is_built_from_resolved_predictions()
    {
        var resolutions = new List<PredictionResolution>
        {
            new(Correct: false, "truth", "claim", Confidence: 0.9),
            new(Correct: false, "truth", "claim", Confidence: 0.9),
            new(Correct: true, "truth", "claim", Confidence: 0.9),
        };

        var profile = Calibration.FromResolutions(resolutions);

        Assert.Equal(3, profile.Samples);

        // Said 90%, right 1/3 — overconfident.
        Assert.True(profile.Overconfidence > 0.5);
    }
}
