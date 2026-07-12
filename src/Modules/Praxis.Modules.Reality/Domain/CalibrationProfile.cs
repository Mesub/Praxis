namespace Praxis.Modules.Reality.Domain;

/// **Calibration, made first-class and made *actionable*.**
///
/// The Twin already keeps a running Brier score per concept (M3.4) — one number, and it is the right
/// number for discounting evidence. But one number **cannot answer the question a learner most needs
/// answered**: *"when I feel sure, am I actually right?"* A Brier of 0.20 can mean *"a bit noisy
/// everywhere"* or *"lethally overconfident at the top, fine in the middle"*, and those are different
/// people needing different advice.
///
/// So the Reality Loop — the fastest-resolving evidence in the product, and therefore the calibration
/// gym — keeps the **reliability curve**: bucket the learner's predictions by how sure they said they
/// were, and for each bucket report how often they were actually right. The gap between *said* and
/// *were* is the miscalibration, and it is **directional** — the one thing a mean Brier throws away.
///
/// ## What it refuses to do
///
/// **It will not diagnose overconfidence from thin data.** Below a sample floor it reports its curve
/// but draws no verdict — a tool that calls someone overconfident on five data points has itself
/// demonstrated overconfidence while lecturing a human about theirs (the exact trap M7.3 named).
///
/// **It tells an abstainer from a miscalibrated learner.** Someone who says "50%" about everything
/// scores a mediocre Brier of ~0.25 — but they are not *wrong* about their confidence, they are
/// **refusing to commit**. Their reliability curve is flat and their *resolution* (below) is near
/// zero, and PRAXIS says *"you are not miscalibrated, you are hedging"* rather than coaching a
/// calibration problem they do not have.
public static class Calibration
{
    /// Below this many resolved predictions, PRAXIS shows the curve but **draws no verdict about the
    /// person.** Five points do not have a shape.
    public const int MinimumForVerdict = 8;

    /// Ten buckets: `[0.0,0.1) … [0.9,1.0]`. The width the reliability diagram is conventionally
    /// drawn at, and fine enough to see overconfidence at the top without starving each bucket.
    public const int BucketCount = 10;

    /// A learner is **hedging** when their forecasts cluster near 0.5 *and* barely move — they say
    /// "maybe" about everything. It is tempting to detect this with resolution alone (an abstainer's
    /// forecasts carry no information), but resolution is also zero for a learner who says the *same
    /// confident number* about everything, and they are the opposite of a hedger. The distinguishing
    /// tell is the confidence itself: hedgers sit near the middle and do not commit.
    public const double HedgingCentre = 0.15;   // mean confidence within this of 0.5
    public const double HedgingSpread = 0.15;    // and the spread of confidences below this

    /// Build a profile straight from resolved Reality-Loop predictions (M9.3, M9.3b). Each resolution
    /// already carries what a forecast needs: how sure the learner was, and whether they were right.
    public static CalibrationProfile FromResolutions(IEnumerable<PredictionResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(resolutions);

        return Build([.. resolutions.Select(r => new Forecast(r.Confidence, r.Correct))]);
    }

    public static CalibrationProfile Build(IReadOnlyList<Forecast> forecasts)
    {
        ArgumentNullException.ThrowIfNull(forecasts);

        var used = forecasts
            .Select(f => new Forecast(Math.Clamp(f.Confidence, 0.0, 1.0), f.Correct))
            .ToList();

        if (used.Count == 0)
        {
            return CalibrationProfile.Empty;
        }

        var buckets = BuildBuckets(used);

        var meanConfidence = used.Average(f => f.Confidence);
        var accuracy = used.Average(f => f.Correct ? 1.0 : 0.0);
        var brier = used.Average(f => Brier(f.Confidence, f.Correct));

        // How much the learner's confidences actually move. A hedger's is tiny; someone who commits
        // to sure/unsure has a wide one.
        var confidenceSpread = Math.Sqrt(used.Average(f => Square(f.Confidence - meanConfidence)));

        // ── Murphy's decomposition: Brier = Reliability − Resolution + Uncertainty ──
        //
        // **Reliability** — how far each bucket's hit-rate sits from the confidence it was made at.
        // This is the miscalibration proper. Zero is perfect. (Lower is better.)
        //
        // **Resolution** — how much the buckets differ from the overall base rate. It measures whether
        // the forecasts *say anything*: a learner who separates the things they will get right from
        // the things they will not has high resolution, even if their absolute levels are off. (Higher
        // is better — and near-zero is the signature of an abstainer.)
        //
        // **Uncertainty** — the irreducible difficulty of the questions themselves, `ō(1−ō)`. Not
        // about the learner at all; it is why raw Brier is not comparable across different item sets.
        var reliability = buckets.Sum(b => b.Count * Square(b.MeanConfidence - b.HitRate)) / used.Count;
        var resolution = buckets.Sum(b => b.Count * Square(b.HitRate - accuracy)) / used.Count;
        var uncertainty = accuracy * (1.0 - accuracy);

        return new CalibrationProfile(
            Samples: used.Count,
            Brier: brier,
            MeanConfidence: meanConfidence,
            ConfidenceSpread: confidenceSpread,

            // **Signed.** Positive means they claimed more certainty than they earned — overconfident,
            // the dangerous direction. Negative means they knew more than they dared say.
            Overconfidence: meanConfidence - accuracy,

            Reliability: reliability,
            Resolution: resolution,
            Uncertainty: uncertainty,
            Buckets: buckets);
    }

    /// The Brier score for one forecast. **Lower is better.** Identical to the Twin's `Calibration.Brier`
    /// on purpose — one definition of the number, wherever it is used.
    public static double Brier(double confidence, bool correct)
    {
        var c = Math.Clamp(confidence, 0.0, 1.0);
        var outcome = correct ? 1.0 : 0.0;

        return (c - outcome) * (c - outcome);
    }

    private static List<ReliabilityBucket> BuildBuckets(IReadOnlyList<Forecast> forecasts)
    {
        var bins = new List<ReliabilityBucket>(BucketCount);

        for (var i = 0; i < BucketCount; i++)
        {
            var lower = i / (double)BucketCount;
            var upper = (i + 1) / (double)BucketCount;

            // The top bucket is closed so that a confidence of exactly 1.0 lands somewhere.
            var inBin = forecasts
                .Where(f => f.Confidence >= lower && (f.Confidence < upper || (i == BucketCount - 1 && f.Confidence <= upper)))
                .ToList();

            bins.Add(new ReliabilityBucket(
                LowerBound: lower,
                UpperBound: upper,
                Count: inBin.Count,
                MeanConfidence: inBin.Count == 0 ? (lower + upper) / 2.0 : inBin.Average(f => f.Confidence),
                HitRate: inBin.Count == 0 ? 0.0 : inBin.Average(f => f.Correct ? 1.0 : 0.0)));
        }

        return bins;
    }

    private static double Square(double x) => x * x;
}

/// One resolved prediction: how sure they said they were, and whether they were right.
public sealed record Forecast(double Confidence, bool Correct);

/// **The reliability curve, and the verdict it does or does not support.**
public sealed record CalibrationProfile(
    int Samples,
    double Brier,

    /// The average confidence they stated — near 0.5 is the hedger's tell.
    double MeanConfidence,

    /// How much their confidence moves. A committed learner's is wide; a hedger's is tiny.
    double ConfidenceSpread,

    /// Mean stated confidence minus actual accuracy. **Positive is overconfident** — the direction
    /// that gets people hurt, because they act on certainty they have not earned.
    double Overconfidence,

    double Reliability,
    double Resolution,
    double Uncertainty,

    IReadOnlyList<ReliabilityBucket> Buckets)
{
    public static CalibrationProfile Empty { get; } =
        new(0, 0, 0.5, 0, 0, 0, 0, 0, []);

    /// **Enough data to say something about the person**, rather than just draw the curve.
    public bool HasVerdict => Samples >= Calibration.MinimumForVerdict;

    /// The accuracy this learner actually achieved, recovered from the two stored means.
    private double Accuracy => MeanConfidence - Overconfidence;

    /// **Not miscalibrated — hedging.** Three things at once: confidence clustered near 0.5, barely
    /// moving, *and* the learner genuinely near a coin flip. All three matter —
    ///
    /// - **near 0.5** rules out someone who says the same *confident* number about everything (also
    ///   zero resolution, but overconfident, not hedging);
    /// - **low spread** rules out a well-calibrated learner whose confidences average to 0.5 because
    ///   they range across the whole scale;
    /// - **near-0.5 accuracy** rules out an *underconfident* learner who mutters "60%" and is right
    ///   90% — their forecasts carry real information, they are just too shy to claim it.
    ///
    /// Miss any one and PRAXIS would coach the wrong problem.
    public bool IsHedging
        => HasVerdict
           && Math.Abs(MeanConfidence - 0.5) < Calibration.HedgingCentre
           && ConfidenceSpread < Calibration.HedgingSpread
           && Math.Abs(Accuracy - 0.5) < Calibration.HedgingCentre;

    /// The one sentence a learner reads. It says what the curve shows, and it **refuses to draw a
    /// verdict the data does not support** — mirroring the diagnostic screen's rule (M7.3) that a
    /// tool must not perform the overconfidence it is measuring.
    public string Headline => (HasVerdict, IsHedging, Overconfidence) switch
    {
        (false, _, _) =>
            "Not enough resolved predictions yet to read your calibration. Keep making them — this "
            + "is the fastest way in the product to find out how well you know what you know.",

        (true, true, _) =>
            "You are not miscalibrated — you are hedging. Almost all your predictions sit near "
            + "'maybe', which is safe and tells you nothing. Commit to a number and find out where "
            + "you actually stand.",

        (true, false, > 0.10) =>
            $"You are overconfident by about {Overconfidence:P0}. When you feel sure, you are right "
            + "less often than you think — which is the expensive direction to be wrong in, because "
            + "you act on it.",

        (true, false, < -0.10) =>
            $"You are underconfident by about {-Overconfidence:P0}. You know more than you are willing "
            + "to claim — which costs you nothing dangerous, but it does cost you.",

        _ => "You are well calibrated: when you say you are sure, you tend to be right. That is rarer "
             + "than being good at the material, and it is worth as much.",
    };
}

/// One point on the reliability diagram: of the predictions made at *this* confidence level, how
/// often the learner was actually right.
public sealed record ReliabilityBucket(
    double LowerBound,
    double UpperBound,
    int Count,

    /// Mean stated confidence of the predictions that landed here.
    double MeanConfidence,

    /// The fraction that turned out correct. **A well-calibrated learner has `HitRate ≈ MeanConfidence`
    /// in every populated bucket** — the reliability curve lies on the diagonal.
    double HitRate)
{
    /// How far this bucket sits off the diagonal, signed. Positive: they were more right than they
    /// claimed here; negative: less.
    public double Gap => Count == 0 ? 0.0 : HitRate - MeanConfidence;
}
