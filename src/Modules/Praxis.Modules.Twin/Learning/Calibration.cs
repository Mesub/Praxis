namespace Praxis.Modules.Twin.Learning;

/// **How well does the learner know what they know?**
///
/// Before every graded item, PRAXIS asks for a confidence. Then it grades them. The gap between the
/// two is calibration, and it is a first-class metric sitting next to mastery — not a footnote.
///
/// ## Why this exists
///
/// A learner who is right 80% of the time and *says* 80% is trustworthy: they can be handed
/// something unfamiliar and will know when to ask for help. A learner who is right 80% of the time
/// and says 99% is dangerous, to themselves and to whoever hires them. Same score. Different person.
///
/// ## The cheating detector that does not detect cheating (C-6)
///
/// A learner who outsources their answers gets **good scores and catastrophic calibration** — they
/// cannot predict their own correctness, because they never produced it. They do not know which
/// ones they got right, because they were not the one getting them right.
///
/// So PRAXIS does not need to *block* cheating. It only needs to *measure* it, discount the
/// assurance on the evidence, and let the Ledger record the truth. This is a far stronger position
/// than proctoring: it does not need to win an arms race, and it degrades gracefully.
public static class Calibration
{
    /// The Brier score for one prediction. **Lower is better.**
    ///
    /// 0.00 — said 100% and was right, or 0% and was wrong. Perfect.
    /// 0.25 — said 50% about everything. The score of a person who has stopped trying to know.
    /// 1.00 — said 100% and was wrong. The worst thing a professional can do.
    public static double Brier(double statedConfidence, bool correct)
    {
        var confidence = Math.Clamp(statedConfidence, 0.0, 1.0);
        var outcome = correct ? 1.0 : 0.0;

        return (confidence - outcome) * (confidence - outcome);
    }

    /// What you get by refusing to commit. If a learner's Brier hovers here, they are not
    /// mis-calibrated — they are *abstaining*, and the UI should say so rather than praise them for
    /// being "about average".
    public const double UninformativeBrier = 0.25;

    /// Below n samples, calibration is noise. Reporting it as knowledge would be exactly the
    /// overconfidence the metric exists to catch.
    public const int MinimumSamplesToReport = 5;

    public static bool IsMeaningful(int samples) => samples >= MinimumSamplesToReport;

    /// Fold one more graded item into a running calibration.
    ///
    /// Kept as an incremental update rather than a re-scan of history because the Twin's projection
    /// applies events one at a time — but note the result is identical to a full recompute, which is
    /// what makes a rebuild reproduce the same numbers.
    public static CalibrationState Observe(
        CalibrationState state,
        double statedConfidence,
        bool correct)
    {
        ArgumentNullException.ThrowIfNull(state);

        var n = state.Samples + 1;
        var confidence = Math.Clamp(statedConfidence, 0.0, 1.0);

        return new CalibrationState(
            Samples: n,
            MeanStatedConfidence: RunningMean(state.MeanStatedConfidence, confidence, n),
            MeanCorrectness: RunningMean(state.MeanCorrectness, correct ? 1.0 : 0.0, n),
            BrierScore: RunningMean(state.BrierScore, Brier(confidence, correct), n));
    }

    private static double RunningMean(double mean, double next, int n)
        => mean + ((next - mean) / n);

    /// **The cheating signature (C-6).**
    ///
    /// High correctness *and* bad calibration. The learner keeps getting things right and keeps
    /// being unable to tell you which ones — which is what it looks like when someone else is
    /// getting them right.
    ///
    /// It is deliberately not called `IsCheating`, and it never blocks anything. It is a reason to
    /// **discount assurance**, so the Ledger records honestly how much of this belief rests on work
    /// we can trust. An honest learner having an unlucky patch is not accused of anything; their
    /// evidence is simply worth a little less until they show they can predict themselves.
    public static bool LooksOutsourced(CalibrationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsMeaningful(state.Samples))
        {
            return false;   // not enough to say anything, and saying it anyway would be the sin
        }

        // Doing well...
        var scoringWell = state.MeanCorrectness >= 0.75;

        // ...and unable to predict their own correctness. Someone who genuinely knows 80% of the
        // material can usually tell you *which* 80%.
        var cannotPredictThemselves = state.BrierScore >= UninformativeBrier;

        return scoringWell && cannotPredictThemselves;
    }

    /// How much to trust evidence produced under this calibration, in [0, 1].
    ///
    /// Applied as a multiplier on the assurance of what the learner produced. Perfect calibration
    /// costs nothing; the uninformative middle costs a little; the cheating signature costs a lot.
    public static double AssuranceMultiplier(CalibrationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!IsMeaningful(state.Samples))
        {
            // No opinion yet. Do not penalise a learner for having just arrived.
            return 1.0;
        }

        if (LooksOutsourced(state))
        {
            return OutsourcedPenalty;
        }

        // Brier of 0 → 1.0. Brier of 0.25 (uninformative) → 0.75. Worse than that → lower still.
        return Math.Clamp(1.0 - state.BrierScore, MinimumMultiplier, 1.0);
    }

    private const double OutsourcedPenalty = 0.35;
    private const double MinimumMultiplier = 0.25;
}

/// A learner's running calibration on one concept, or across everything.
///
/// `Overconfidence` is the Dunning–Kruger delta: what they *said* minus what they *did*.
public sealed record CalibrationState(
    int Samples,
    double MeanStatedConfidence,
    double MeanCorrectness,
    double BrierScore)
{
    public static readonly CalibrationState Empty = new(0, 0.0, 0.0, 0.0);

    /// **Positive: they think they are better than they are.** The most dangerous state a learner
    /// can be in — not because it feels bad, but because they will not study it.
    ///
    /// Negative: they are better than they believe, and are wasting their time revising something
    /// they already hold. Worth telling them too.
    public double Overconfidence => MeanStatedConfidence - MeanCorrectness;
}
