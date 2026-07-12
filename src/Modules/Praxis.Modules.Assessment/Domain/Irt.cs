namespace Praxis.Modules.Assessment.Domain;

/// **Item Response Theory: what one answer actually tells you about a person.**
///
/// A raw score — *"you got 7 out of 10"* — is almost meaningless, because it does not say **which**
/// seven. Seven easy ones and three hard ones is a completely different mind from three easy ones
/// and seven hard ones, and a percentage cannot tell them apart. IRT can: it models the learner's
/// ability (`θ`, in logits) and each item's difficulty (`b`, on the same scale), so a correct answer
/// to a hard item moves belief a great deal and a correct answer to a trivial one moves it barely at
/// all.
///
/// ## Why this is 3PL and not the 2PL the roadmap asked for
///
/// The roadmap says *"2PL model"*. CANON's own item schema carries a **`c`** — a guessing floor —
/// and CANON is right.
///
/// On a four-option multiple-choice item, a learner who knows **nothing at all** still scores about
/// 25%. A 2PL model has no way to express that: it reads those lucky hits as *evidence of ability*
/// and quietly inflates `θ` — and it does it worst for exactly the learners who are struggling most,
/// which is precisely the population whose model must not be flattering.
///
/// So: three parameters, and `c = 0` is the default. **With no guessing, this *is* 2PL** — the
/// roadmap's model is the special case, not a different one. Nothing is lost, and the MCQ case stops
/// lying.
public static class Irt
{
    /// The scale. `θ` and `b` are both in **logits**, and they mean the same thing on the same ruler:
    /// an item at `b = θ` is one the learner has a ~50% chance of getting right (before guessing).
    public const double MinTheta = -4.0;
    public const double MaxTheta = 4.0;

    /// **P(correct | θ)** — the three-parameter logistic.
    ///
    /// * `a` — *discrimination*. How sharply this item separates people who know it from people who
    ///   do not. A low `a` is a woolly item: everyone has much the same chance, so it tells you
    ///   nothing about anybody.
    /// * `b` — *difficulty*, on the same scale as θ.
    /// * `c` — *the guessing floor*. The probability a learner who knows nothing still gets it right.
    ///   0 for free response; 0.25 for a four-option MCQ. **Not a fudge factor** — it is the honest
    ///   admission that a multiple-choice question can be answered by a coin.
    public static double Probability(double theta, double a, double b, double c = 0.0)
    {
        Guard(a, c);

        var logistic = 1.0 / (1.0 + Math.Exp(-a * (theta - b)));

        return c + ((1.0 - c) * logistic);
    }

    /// **Fisher information** — how much this item would actually *tell you* about this learner.
    ///
    /// It peaks when the item is matched to the learner, and collapses to nothing when it is far too
    /// easy or far too hard. That is the whole argument against a fixed test: half the questions on
    /// it are telling you nothing about the person in front of you, and they are still taking up
    /// their afternoon.
    public static double Information(double theta, double a, double b, double c = 0.0)
    {
        Guard(a, c);

        var p = Probability(theta, a, b, c);

        if (p <= c || p >= 1.0)
        {
            return 0.0;
        }

        // The 3PL information function. With c = 0 this reduces to a²·p·(1−p) — the 2PL form.
        var numerator = a * a * (1.0 - p) * Math.Pow(p - c, 2.0);
        var denominator = p * Math.Pow(1.0 - c, 2.0);

        return numerator / denominator;
    }

    /// **The Twin's posterior, updated by what they actually did** (EAP — expected a posteriori).
    ///
    /// It starts from a prior, not from zero. **A standard adaptive test starts every learner at the
    /// population mean, which is a way of saying it has decided to know nothing about you** — and
    /// PRAXIS already has a probabilistic model of this mind (I-1). Throwing that away to start from
    /// θ=0 would be discarding the entire product for the sake of a textbook.
    ///
    /// Quadrature over a fixed grid: exact enough, deterministic, and — crucially — **it cannot fail
    /// to converge**, which an iterative maximiser can and does, usually on the learner who answered
    /// everything wrong.
    public static Ability Update(Ability prior, IReadOnlyList<Response> responses)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(responses);

        const int points = 161;   // −4.0 … 4.0 in steps of 0.05

        var step = (MaxTheta - MinTheta) / (points - 1);

        double weightSum = 0.0;
        double thetaSum = 0.0;
        double thetaSquaredSum = 0.0;

        for (var i = 0; i < points; i++)
        {
            var theta = MinTheta + (i * step);

            // The prior: what the Twin already believed about this person.
            var z = (theta - prior.Theta) / prior.StandardError;
            var weight = Math.Exp(-0.5 * z * z);

            // The likelihood: what they just did.
            foreach (var response in responses)
            {
                var p = Probability(theta, response.A, response.B, response.C);

                weight *= response.Correct ? p : 1.0 - p;
            }

            weightSum += weight;
            thetaSum += theta * weight;
            thetaSquaredSum += theta * theta * weight;
        }

        if (weightSum <= 0 || double.IsNaN(weightSum))
        {
            // Every grid point has vanishing likelihood — which means the responses are impossible
            // under this model, not that the learner is impossible. We keep the prior rather than
            // inventing a number, because a θ produced by a numerical accident is worse than no θ.
            return prior;
        }

        var mean = thetaSum / weightSum;
        var variance = Math.Max(0.0, (thetaSquaredSum / weightSum) - (mean * mean));

        return new Ability(
            Math.Clamp(mean, MinTheta, MaxTheta),
            Math.Max(Math.Sqrt(variance), 0.01));
    }

    private static void Guard(double a, double c)
    {
        if (a <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(a), a,
                "Discrimination must be positive. A zero or negative `a` says that knowing more "
                + "makes you *less* likely to get this right, which is not an item — it is a bug in "
                + "the item.");
        }

        if (c is < 0.0 or >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(c), c,
                "A guessing floor of 1.0 means everyone gets it right without knowing anything. "
                + "That is not an item either.");
        }
    }
}

/// What PRAXIS believes about a learner's ability on one concept, and **how sure it is**.
///
/// The standard error is not decoration. It is the difference between *"we think you are about
/// here"* and *"we know you are here"*, and it is the thing that decides whether PRAXIS is entitled
/// to stop asking.
public sealed record Ability(double Theta, double StandardError)
{
    /// A learner PRAXIS knows nothing about. **Wide, and honestly so** — a narrow prior on no
    /// evidence is a confident lie.
    public static Ability Unknown { get; } = new(0.0, 1.5);

    /// Below this, PRAXIS is entitled to stop asking about θ itself (CANON §4.3).
    public const double ConfidentSe = 0.30;

    public bool IsConfident => StandardError < ConfidentSe;
}

/// One thing the learner did, and the item they did it on.
public sealed record Response(bool Correct, double A, double B, double C = 0.0);
