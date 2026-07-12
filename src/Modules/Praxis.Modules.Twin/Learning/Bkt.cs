namespace Praxis.Modules.Twin.Learning;

/// **Bayesian Knowledge Tracing.** How one graded answer changes what we believe.
///
/// Four numbers describe a learner's relationship with a concept:
///
///   P(L)  — the probability they know it *now*. This is what we are updating.
///   P(T)  — transit: the chance an opportunity to practise *teaches* it to them.
///   P(S)  — slip: they know it, and got it wrong anyway. Fatigue, a typo, a misread question.
///   P(G)  — guess: they do not know it, and got it right anyway. Luck, or four options.
///
/// **Slip and guess are why a single answer is weak evidence**, and why PRAXIS never lets one
/// decide anything. A correct answer on a multiple-choice item is one part signal and one part
/// coin flip; the maths here says exactly how much of each, and refuses to pretend otherwise.
///
/// Everything in this file is a **pure function**. No clock, no randomness, no model call. Replay
/// the same events and you get the same beliefs, byte for byte — which is the promise M1.6 tests
/// and the reason the Ledger can be verified at all.
public static class Bkt
{
    /// Update belief given one graded observation.
    ///
    /// Two steps, and they are different things:
    ///   1. **Infer** — given what they just did, what did they know *at the moment they did it*?
    ///   2. **Learn** — having had the opportunity, what do they know *now*?
    ///
    /// Conflating them is the classic BKT bug: it lets a wrong answer still raise belief, because
    /// the learning step is applied before the evidence is taken into account.
    public static double Observe(double pKnows, bool correct, BktParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);

        var prior = Math.Clamp(pKnows, 0.0, 1.0);

        // Step 1 — Bayes. What did they know when they answered?
        //
        // Two ways to produce what we observed: they knew it, or they did not. Weigh them.
        var (fromKnowing, fromNotKnowing) = correct
            ? (prior * (1 - p.Slip), (1 - prior) * p.Guess)          // knew and did not slip · guessed
            : (prior * p.Slip, (1 - prior) * (1 - p.Guess));         // knew and slipped · did not know, did not guess

        var posterior = Posterior(fromKnowing, fromNotKnowing);

        // Step 2 — the practice itself was an opportunity to learn.
        var updated = posterior + ((1 - posterior) * p.Transit);

        return Math.Clamp(updated, MinBelief, MaxBelief);
    }

    /// **Certainty is an absorbing state, and it makes the Twin unfalsifiable.**
    ///
    /// The transit step drives belief asymptotically towards 1, and in floating point it *arrives*.
    /// At exactly 1.0 the Bayesian update dies: the "they did not know it" branch is weighted by
    /// (1 − P) = 0, so no evidence — not one failure, not fifty — can ever move belief again. The
    /// Twin would insist a learner knows something while watching them get it wrong forever.
    ///
    /// The same trap sits at 0: a learner we are *certain* knows nothing can never be shown to have
    /// learned it.
    ///
    /// So belief lives strictly inside (0, 1). This is not numerical hygiene. It is the difference
    /// between a model of a person and a verdict about them.
    public const double MaxBelief = 0.99;
    public const double MinBelief = 0.01;

    private static double Posterior(double fromKnowing, double fromNotKnowing)
    {
        var evidence = fromKnowing + fromNotKnowing;

        // Both explanations impossible at once. Only reachable with degenerate parameters, which
        // BktParameters refuses to construct — so this is a guard, not a code path.
        return evidence <= double.Epsilon ? 0.0 : fromKnowing / evidence;
    }
}

/// The four BKT parameters for one learner on one concept.
///
/// Seeded from the concept's difficulty prior (the graph's opinion) and **replaced by measurement**
/// once enough responses exist. An estimate reality never corrects is an opinion with a decimal
/// point.
public sealed class BktParameters
{
    /// P(T): the chance one opportunity to practise moves them from not-knowing to knowing.
    public double Transit { get; }

    /// P(S): knew it, got it wrong.
    public double Slip { get; }

    /// P(G): did not know it, got it right.
    public double Guess { get; }

    private BktParameters(double transit, double slip, double guess)
    {
        Transit = transit;
        Slip = slip;
        Guess = guess;
    }

    /// **The identifiability constraint**, and it is not a nicety.
    ///
    /// If `slip + guess >= 1`, the model becomes *degenerate*: a correct answer is evidence you do
    /// NOT know the concept, because a knower is more likely to slip than a guesser is to guess.
    /// The Twin would invert, silently, and confidently — teaching harder to the people who are
    /// getting things right.
    ///
    /// This is a well-known failure mode of fitted BKT, and it is refused at construction rather
    /// than hoped away.
    public const double MaxSlipPlusGuess = 0.95;

    /// Seed from the concept's difficulty prior (IRT logit, roughly -3 easy to +3 brutal) and the
    /// kind of evidence being produced.
    ///
    /// The intuitions, stated so they can be argued with:
    ///
    /// - **Harder concepts slip more.** Under load, a shaky grasp fails in ways a solid one does
    ///   not.
    /// - **Harder concepts are guessed less.** You can guess a definition. You cannot guess a
    ///   working retrieval pipeline.
    /// - **Harder concepts transit more slowly.** One good session teaches you tokenisation. It
    ///   does not teach you to beat BM25.
    public static BktParameters ForDifficulty(double difficultyPrior)
    {
        var d = Math.Clamp(difficultyPrior, -3.0, 3.0);

        // Map [-3, 3] onto [0, 1] — 0 is trivial, 1 is brutal.
        var hardness = (d + 3.0) / 6.0;

        var transit = Lerp(0.35, 0.08, hardness);   // easy things stick fast
        var slip = Lerp(0.05, 0.20, hardness);      // hard things fail even when known
        var guess = Lerp(0.25, 0.05, hardness);     // hard things cannot be guessed

        return Create(transit, slip, guess);
    }

    public static BktParameters Create(double transit, double slip, double guess)
    {
        if (transit is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(transit), transit, "P(T) is a probability.");
        }

        if (slip is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(slip), slip, "P(S) is a probability.");
        }

        if (guess is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(guess), guess, "P(G) is a probability.");
        }

        if (slip + guess >= MaxSlipPlusGuess)
        {
            throw new ArgumentException(
                $"slip ({slip:F2}) + guess ({guess:F2}) = {slip + guess:F2}, which is degenerate. "
                + "In this regime a CORRECT answer becomes evidence the learner does NOT know the "
                + "concept, because a knower is likelier to slip than a guesser is to guess. The "
                + "Twin would invert — silently, and confidently — and PRAXIS would teach hardest "
                + "to the people getting things right.",
                nameof(slip));
        }

        return new BktParameters(transit, slip, guess);
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
}
