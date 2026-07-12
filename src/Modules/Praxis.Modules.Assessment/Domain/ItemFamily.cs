namespace Praxis.Modules.Assessment.Domain;

/// **You cannot calibrate an item from one response. So calibrate the family.**
///
/// PRAXIS generates every item fresh, for one learner, once (M5.5) — which is what makes `APPLY`
/// evidence mean anything. It also means every item has been seen by exactly **one person**, and
/// `n = 1` is not a sample. Anyone who claims to have calibrated an item on one response is
/// hand-waving, and the number they produce will look exactly like a real one.
///
/// The way out is the **item family**: a template with a deep structure, from which instances are
/// drawn with the surface features randomised. The *family* is the calibration unit. The *instance*
/// inherits the family's posterior.
///
/// > **A family with a large spread in difficulty is not a family. It is a bag.**
///
/// That is the sentence to remember. If instances of a "family" turn out to vary wildly in how hard
/// they are, then the thing binding them together was never a deep structure — it was a folder name,
/// and every difficulty estimate inherited from it is noise wearing a decimal point.
///
/// ## Three stages, and PRAXIS does not pretend to be in the last one
///
/// * **Cold** (`n < 30`) — difficulty comes from an LLM's estimate blended with a feature model. It
///   is a *prior*, and it is called one.
/// * **Warming** (`30 ≤ n < 200`) — shrinkage. The prior and the data are blended by how much data
///   there actually is: `λ = τ² / (τ² + σ²/n)`. Early on, the prior dominates and *should*.
/// * **Calibrated** (`n ≥ 200`) — the data has earned the right to speak for itself.
///
/// ## And the honest thing PRAXIS does while it waits
///
/// **Provisional responses count immediately, but weakly.** A response to an uncalibrated family
/// updates the Twin at `κ × 0.6` until the family is calibrated, and is **re-weighted retroactively**
/// on the nightly rebuild — which the event log makes trivially correct, because the Twin is a
/// projection and can simply be recomputed with better numbers.
///
/// The alternative — withholding a learner's evidence until the statistics catch up — would mean a
/// learner does real work on Tuesday and PRAXIS pretends it did not happen. **We do not withhold
/// learning signal while we wait for statistics. We discount it, and we come back and fix it.**
public sealed class ItemFamily
{
    /// Below this, the family is `Cold` — the difficulty is a prior, and PRAXIS says so.
    public const int WarmingAt = 30;

    /// At this many responses the family has earned the right to speak for itself.
    public const int CalibratedAt = 200;

    /// **The discount on evidence from an uncalibrated family** (CANON §3.5).
    ///
    /// Not zero — a learner's work counts the moment they do it. Not one — PRAXIS does not pretend
    /// to know how hard the item was when it does not.
    public const double ProvisionalGain = 0.6;

    /// **The spread beyond which this is not a family, it is a bag.**
    ///
    /// If instances vary by more than this in difficulty, the "deep structure" binding them was a
    /// folder name.
    public const double MaxHealthySpread = 0.8;

    public string Id { get; }

    /// Discrimination. How sharply this family separates people who know it from people who do not.
    public double A { get; private set; }

    /// The current best estimate of difficulty, in logits. **What this *means* depends on the
    /// stage** — in `Cold` it is a guess with a decimal point, and calling it anything else would be
    /// a lie told with statistics.
    public double B { get; private set; }

    /// The guessing floor. 0 for free response; 0.25 for a four-option MCQ.
    public double C { get; }

    /// What the LLM (blended with the feature model) thought before anybody answered anything.
    public double PriorB { get; }

    /// How much the prior is trusted, as a standard deviation in logits. A *tight* prior on an
    /// LLM's opinion would be absurd.
    public double PriorSpread { get; }

    public int Responses { get; private set; }

    /// The observed spread in instance difficulty. **Watched, because a family with a large spread
    /// is a bag.**
    public double ObservedSpread { get; private set; }

    public CalibrationStage Stage => Responses switch
    {
        >= CalibratedAt => CalibrationStage.Calibrated,
        >= WarmingAt => CalibrationStage.Warming,
        _ => CalibrationStage.Cold,
    };

    /// **Is this actually a family?** A spread this wide means the instances have nothing in common
    /// but a name, and every difficulty inherited from it is noise.
    public bool IsABag => Responses >= WarmingAt && ObservedSpread > MaxHealthySpread;

    /// **What one response from this family is worth to the Twin.**
    ///
    /// Full weight when the family is calibrated; discounted while it is not — and, crucially, the
    /// discount is applied to the *Twin's* belief update, never to the learner's Ledger entry. They
    /// did the work. What is uncertain is *how hard it was*, and that is our problem, not theirs.
    public double UpdateGainMultiplier
        => Stage is CalibrationStage.Calibrated ? 1.0 : ProvisionalGain;

    private ItemFamily(string id, double a, double priorB, double priorSpread, double c)
    {
        Id = id;
        A = a;
        B = priorB;
        C = c;
        PriorB = priorB;
        PriorSpread = priorSpread;
    }

    /// **Seeded from a prior, and it is *called* a prior.**
    ///
    /// `b̂ = 0.6 · LLM_estimate + 0.4 · feature_model` (CANON §3.5). The model gets the larger share
    /// because it has read the concept; the feature model — inference steps, new terms, stem length,
    /// prerequisite depth — is the thing that stops it drifting into whatever the model happens to
    /// find impressive that afternoon.
    public static ItemFamily Seed(
        string id,
        double llmDifficulty,
        double featureDifficulty,
        double discrimination = 1.0,
        double guessing = 0.0,
        double priorSpread = 0.8)
    {
        if (priorSpread <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priorSpread), priorSpread,
                "A prior with no spread is a certainty. PRAXIS is not certain about an item nobody "
                + "has ever answered.");
        }

        var seeded = (0.6 * llmDifficulty) + (0.4 * featureDifficulty);

        return new ItemFamily(id, discrimination, seeded, priorSpread, guessing);
    }

    /// **Shrinkage.** `λ = τ² / (τ² + σ²/n)` — the prior and the data blended by how much data there
    /// actually is.
    ///
    /// This is the honest version of *"we learn from responses"*. With three responses the prior
    /// dominates, and it **should**: three people is not evidence about difficulty, it is an anecdote
    /// with error bars. With two hundred, the data speaks for itself and the prior fades out of the
    /// arithmetic entirely.
    ///
    /// The thing to notice is that nobody has to *decide* when to switch. The formula does it, and
    /// it does it continuously — which is why there is no cliff edge where a family suddenly becomes
    /// trustworthy overnight.
    /// **A bug worth recording**, because the first version of this looked completely reasonable.
    ///
    /// I shrank towards *the observation that had just arrived*, rather than towards the **mean of
    /// all the observations so far**. At `n = 60` the shrinkage weight is 0.97, so `B` simply chased
    /// whatever the last learner did — and the measured spread, being the distance from `B` to that
    /// same observation, collapsed to nearly **zero**.
    ///
    /// The consequence was quiet and severe: **a bag would have looked like a perfectly tight
    /// family.** Instances scattered across five logits, `ObservedSpread ≈ 0`, `IsABag = false`, and
    /// every item drawn from it inheriting a difficulty that meant nothing. The `IsABag` check would
    /// have been decoration — a guard that could never fire.
    ///
    /// Shrinkage is a statement about a **distribution**, not about the last thing that happened.
    private double _sum;
    private double _sumOfSquares;

    public void Observe(double observedDifficulty, double observationNoise = 1.0)
    {
        Responses++;

        _sum += observedDifficulty;
        _sumOfSquares += observedDifficulty * observedDifficulty;

        var mean = _sum / Responses;

        var tauSquared = PriorSpread * PriorSpread;
        var sigmaSquared = observationNoise * observationNoise;

        var lambda = tauSquared / (tauSquared + (sigmaSquared / Responses));

        // λ → 1 as n grows: the data wins. λ → 0 when n is tiny: the prior holds. And what the data
        // *says* is its mean — not its most recent member.
        B = ((1.0 - lambda) * PriorB) + (lambda * mean);

        // **How far the instances actually scatter.** Measured against the mean, because that is the
        // question: do these items agree with *each other* about how hard they are?
        var variance = Math.Max(0.0, (_sumOfSquares / Responses) - (mean * mean));

        ObservedSpread = Math.Sqrt(variance);
    }
}

public enum CalibrationStage
{
    /// Fewer than 30 responses. **The difficulty is a prior, and PRAXIS calls it one.**
    Cold = 1,

    /// Shrinking towards the data as the data arrives.
    Warming = 2,

    /// 200+ responses. The data has earned the right to speak for itself.
    Calibrated = 3,
}
