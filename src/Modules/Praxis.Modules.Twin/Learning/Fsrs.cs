namespace Praxis.Modules.Twin.Learning;

/// **Forgetting.** The thing every other learning product pretends does not happen.
///
/// Two quantities, and they are not the same:
///
///   **Stability (S)** — how *durable* the memory is, in days. Formally: the number of days until
///   retrievability falls to 90%. It grows with each successful retrieval and collapses on a lapse.
///
///   **Retrievability (R)** — how *available* it is right now, given how long it has been. A
///   probability, falling with elapsed time along a power curve.
///
/// A memory can be highly stable and currently unavailable — you *will* remember it, given a
/// second. That is why PRAXIS schedules a rep instead of re-teaching the concept.
///
/// The curve is a **power law, not an exponential**. This matters: exponential forgetting says a
/// memory you have held for a year is as fragile per-day as one you learned yesterday, which is
/// false and produces schedules that nag people about things they will never forget.
///
/// Everything here is a pure function of (stability, elapsed days). No clock is read inside — the
/// caller passes elapsed time, because a projection that reads the wall clock is not replayable and
/// the Ledger would stop being verifiable.
public static class Fsrs
{
    /// The FSRS power-curve constants. `Decay` is the exponent; `Factor` is derived from it so that
    /// R(S days) = 0.9 exactly — which is what makes "stability" mean something you can state in
    /// English rather than a fitted magic number.
    public const double Decay = -0.5;

    public static readonly double Factor = Math.Pow(0.9, 1.0 / Decay) - 1.0;

    /// The probability the learner could retrieve this *right now*.
    ///
    /// R(0) = 1. R(S) = 0.9, by construction. Then it falls away, slower and slower.
    public static double Retrievability(double stability, double daysElapsed)
    {
        if (stability <= 0)
        {
            // Never learned, or lapsed to nothing. There is nothing to retrieve.
            return 0.0;
        }

        var elapsed = Math.Max(0.0, daysElapsed);

        return Math.Pow(1.0 + (Factor * elapsed / stability), Decay);
    }

    /// How long until retrievability falls to `targetRetention`. The rep queue's whole job.
    ///
    /// At the default 0.90 this returns exactly `stability` — that is what stability *is*.
    public static double DaysUntil(double stability, double targetRetention)
    {
        if (stability <= 0)
        {
            return 0.0;
        }

        var target = Math.Clamp(targetRetention, 0.01, 0.99);

        return stability / Factor * (Math.Pow(target, 1.0 / Decay) - 1.0);
    }

    /// **Belief decays towards the prior, not towards zero.**
    ///
    /// A forgotten concept does not leave you knowing *less than a stranger*. It leaves you knowing
    /// what a stranger knows — which for a well-designed multiple-choice item is not nothing, and
    /// for a build task is close to it.
    ///
    /// Decaying to zero would also make the Twin claim a certainty it has not earned: "this person
    /// definitely cannot do this", of someone who did it perfectly a year ago.
    public static double DecayBelief(double pKnows, double prior, double stability, double daysElapsed)
    {
        var retained = Retrievability(stability, daysElapsed);

        // As R → 0, belief → prior. As R → 1, belief is untouched.
        return prior + ((pKnows - prior) * retained);
    }

    /// Stability after a **successful** retrieval.
    ///
    /// The heart of spaced repetition: **the harder it was to recall, the more recalling it helped.**
    /// A rep you aced with the answer fresh in mind teaches you almost nothing; one you dragged back
    /// from the edge of forgetting is worth weeks.
    ///
    /// So the gain is *largest* when retrievability was *lowest*. This is the difference between a
    /// scheduler and a nagging app.
    public static double StabilityAfterRecall(
        double stability,
        double difficulty,
        double retrievabilityAtReview)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);
        var r = Math.Clamp(retrievabilityAtReview, 0.0, 1.0);

        if (stability <= 0)
        {
            // First success. Start from a base that a hard concept earns less of.
            return InitialStability(d);
        }

        // Harder concepts consolidate less per rep.
        var difficultyPenalty = 11.0 - d;

        // Diminishing returns: a very stable memory gains proportionally less.
        var saturation = Math.Pow(stability, -0.15);

        // THE POINT: retrieving something you had nearly forgotten is worth far more than
        // retrieving something you just saw.
        var retrievalBonus = Math.Exp(RetrievalWeight * (1.0 - r)) - 1.0;

        var growth = 1.0 + (GrowthWeight * difficultyPenalty * saturation * retrievalBonus);

        return stability * Math.Max(1.0, growth);
    }

    /// Stability after a **lapse** — they could not retrieve it.
    ///
    /// It collapses, but it does not reset to zero: a relearned memory is easier to relearn again,
    /// and pretending otherwise makes the scheduler treat a returning learner like a beginner.
    public static double StabilityAfterLapse(double stability, double difficulty)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);

        if (stability <= 0)
        {
            return 0.0;
        }

        // Harder concepts lapse harder.
        var retained = LapseFloor * Math.Pow(stability, LapseExponent) * ((11.0 - d) / 10.0);

        return Math.Clamp(retained, MinStabilityAfterLapse, stability);
    }

    /// **Difficulty is the learner's, not the concept's.**
    ///
    /// The graph has an opinion about how hard a concept is. This is how hard *this person* finds
    /// it, revealed by what they actually do — and it is pulled towards the truth by every outcome,
    /// with failure moving it harder than success.
    public static double UpdateDifficulty(double difficulty, bool correct)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);

        var delta = correct ? -DifficultyStep : DifficultyStep * FailureWeight;

        return Math.Clamp(d + delta, 1.0, 10.0);
    }

    /// What one first success is worth, before there is any history. A brutal concept earns less
    /// stability from its first correct answer than a trivial one — because it is likelier to have
    /// been a fluke.
    public static double InitialStability(double difficulty)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);

        // 1 day at the hardest, ~4 days at the easiest.
        return Math.Max(MinStability, 4.5 - (0.35 * d));
    }

    // ── The knobs. Fitted to real review data in a real system; priors here, and every one of
    //    them is a claim about human memory that cohort data will eventually correct. ──────────

    private const double RetrievalWeight = 2.2;
    private const double GrowthWeight = 0.08;

    private const double LapseFloor = 1.5;
    private const double LapseExponent = 0.35;
    private const double MinStabilityAfterLapse = 0.5;

    private const double DifficultyStep = 0.3;
    private const double FailureWeight = 2.0;

    private const double MinStability = 0.5;
}
