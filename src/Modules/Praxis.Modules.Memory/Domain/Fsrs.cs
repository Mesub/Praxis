namespace Praxis.Modules.Memory.Domain;

/// **Forgetting** — the thing every other learning product pretends does not happen.
///
/// This is FSRS-5, implemented from CANON §3.3 rather than approximated. Three variables:
///
///   **Difficulty (D)**, in [1, 10] — how hard *this learner* finds *this concept*. Not how hard the
///   concept is. The Graph has an opinion about that; this is the truth revealed by what they do.
///
///   **Stability (S)**, in days — **the interval at which retrievability falls to exactly 0.90.**
///   That identity is not a description, it is the *definition*, and it is load-bearing: it is what
///   lets PRAXIS say *"you will remember this for eleven days"* in English rather than reporting a
///   fitted magic number.
///
///   **Retrievability (R)**, in [0, 1] — the probability you could retrieve it **right now**.
///
/// A memory can be highly stable and currently unavailable — you *will* remember it, given a second.
/// That is why PRAXIS schedules a rep rather than re-teaching the concept from scratch.
///
/// ## The curve is a power law, not an exponential
///
/// Exponential forgetting says a memory you have held for a year is as fragile *per day* as one you
/// learned yesterday. That is false, and it produces schedulers that nag people about things they
/// are never going to forget.
///
/// ## Nothing here reads a clock
///
/// Every function is a pure function of its arguments; elapsed time is *passed in*. A projection that
/// reads the wall clock is not replayable, and the moment the Twin is not replayable the Ledger's
/// claims stop being verifiable.
///
/// ## And it refuses to reward cramming
///
/// See `Next`. This is the one place where a *memory model* enforces a *product refusal*, and it does
/// so without any policy code at all — see `MassedPractice` for what actually had to be true for the
/// claim to hold, and what did not.
public static class Fsrs
{
    /// The FSRS-4.5+ power curve. `Decay` is the exponent; `Factor` is **derived** from it so that
    /// `R(S) = 0.9` exactly. That is what makes stability mean something sayable out loud.
    public const double Decay = -0.5;

    /// `0.9^(1/DECAY) − 1 = 19/81`. Derived, never typed in — a hard-coded 0.2346 is a number that
    /// silently stops matching `Decay` the day somebody tunes it.
    public static readonly double Factor = Math.Pow(0.9, 1.0 / Decay) - 1.0;

    /// **The published FSRS-5 weights, used as the population prior.**
    ///
    /// Every one of these nineteen numbers is a *claim about human memory*, fitted to millions of
    /// real reviews by people who are not us. PRAXIS refits them per learner once there are ≥ 400
    /// reviews to fit on, and per domain pack — but it starts here, honestly, with somebody else's
    /// evidence rather than our guesses.
    ///
    /// The previous implementation used numbers I made up. They were plausible. They were not
    /// evidence, and a plausible number in a model of somebody's memory is just a confident guess
    /// wearing a lab coat.
    public static readonly double[] W =
    [
        0.40255, 1.18385, 3.17300, 15.69105, 7.19490, 0.53450, 1.46040, 0.00460, 1.54575,
        0.11920, 1.01925, 1.93950, 0.11000, 0.29605, 2.26980, 0.23150, 2.98980, 0.51655, 0.66210,
    ];

    public const double MinStability = 0.01;
    public const double MaxStability = 36_500.0;

    /// The probability the learner could retrieve this **right now**.
    ///
    /// `R(0) = 1`. `R(S) = 0.9`, by construction. Then it falls away, slower and slower.
    public static double Retrievability(double stability, double daysElapsed)
    {
        if (stability <= 0)
        {
            // Never learned, or lapsed to nothing. There is nothing there to retrieve.
            return 0.0;
        }

        var elapsed = Math.Max(0.0, daysElapsed);

        return Math.Pow(1.0 + (Factor * elapsed / stability), Decay);
    }

    /// How many days until retrievability falls to `targetRetention`. **The rep queue's whole job.**
    ///
    /// At the default 0.90 this returns exactly `stability` — because that is what stability *is*.
    public static double Interval(double stability, double targetRetention)
    {
        if (stability <= 0)
        {
            return 0.0;
        }

        var target = Math.Clamp(targetRetention, 0.01, 0.99);

        return stability / Factor * (Math.Pow(target, 1.0 / Decay) - 1.0);
    }

    /// Stability after the **first** successful retrieval. `S₀(G) = w[G−1]`.
    ///
    /// 0.40 days for a barely-scraped `Again`, 15.69 for an `Easy`. The spread is enormous and it is
    /// meant to be: getting something right *effortlessly* the first time is a completely different
    /// fact about a person than getting it right by the skin of their teeth.
    public static double InitialStability(Grade grade)
        => Math.Clamp(W[(int)grade - 1], MinStability, MaxStability);

    /// Difficulty after the first review. `D₀(G) = w₄ − e^{w₅·(G−1)} + 1`.
    public static double InitialDifficulty(Grade grade)
        => Math.Clamp(W[4] - Math.Exp(W[5] * ((int)grade - 1)) + 1.0, 1.0, 10.0);

    /// **The whole model, in one call.**
    ///
    /// Give it where the learner was, what they just did, and how long it had been. It gives back
    /// where they are now.
    ///
    /// `daysElapsed` is time since the **last review**, not since the concept was learned — the model
    /// cares how faded the memory was *at the moment they reached for it*, because that is what
    /// determines what reaching for it was worth.
    public static MemoryState Next(MemoryState state, Grade grade, double daysElapsed)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsNew)
        {
            return new MemoryState(
                Stability: InitialStability(grade),
                Difficulty: InitialDifficulty(grade));
        }

        var elapsed = Math.Max(0.0, daysElapsed);
        var r = Retrievability(state.Stability, elapsed);

        var difficulty = NextDifficulty(state.Difficulty, grade);

        // **There is no special case for a same-day review, and that is the point.** See
        // `MassedPractice` below for why the obvious one is a trap.
        var stability = grade is Grade.Again
            ? AfterLapse(state.Stability, state.Difficulty, r)
            : AfterRecall(state.Stability, state.Difficulty, r, grade);

        return new MemoryState(
            Stability: Math.Clamp(stability, MinStability, MaxStability),
            Difficulty: difficulty);
    }

    /// Stability after a **successful, properly spaced** review.
    ///
    /// `S' = S · [ 1 + e^{w₈}·(11 − D)·S^{−w₉}·(e^{w₁₀·(1−R)} − 1)·h·b ]`
    ///
    /// Read the terms. Stability grows **more** when difficulty is low, when stability is already
    /// small, and — the term that matters — **when retrievability was low.**
    ///
    /// `e^{w₁₀·(1−R)} − 1` is the **desirable-difficulty term**, and it is the whole of spaced
    /// repetition in one expression: *dragging something back from the edge of forgetting is worth
    /// enormously more than glancing at something you just saw.* At `R = 1` it is exactly **zero** —
    /// a review of something perfectly fresh buys you **nothing**, and the model says so in
    /// arithmetic rather than in a warning banner.
    public static double AfterRecall(double stability, double difficulty, double r, Grade grade)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);
        var retrievability = Math.Clamp(r, 0.0, 1.0);

        if (stability <= 0)
        {
            return InitialStability(grade);
        }

        var hard = grade is Grade.Hard ? W[15] : 1.0;
        var easy = grade is Grade.Easy ? W[16] : 1.0;

        var growth = 1.0
            + (Math.Exp(W[8])
               * (11.0 - d)
               * Math.Pow(stability, -W[9])
               * (Math.Exp(W[10] * (1.0 - retrievability)) - 1.0)
               * hard
               * easy);

        return stability * Math.Max(1.0, growth);
    }

    /// Stability after a **lapse** — they reached for it and it was not there.
    ///
    /// `S'_fail = w₁₁ · D^{−w₁₂} · ((S + 1)^{w₁₃} − 1) · e^{w₁₄·(1−R)}`, then `S' = min(S'_fail, S)`.
    ///
    /// **A lapse can never increase stability.** That `min` is not a safety clamp, it is a statement:
    /// forgetting something does not make it more durable, and a model that let it would eventually
    /// tell a learner their worst concept was their strongest.
    ///
    /// But it does not reset to zero either. A relearned memory is easier to relearn, and pretending
    /// otherwise makes the scheduler treat a returning learner like a beginner — which is both wrong
    /// and insulting.
    public static double AfterLapse(double stability, double difficulty, double r)
    {
        if (stability <= 0)
        {
            return 0.0;
        }

        var d = Math.Clamp(difficulty, 1.0, 10.0);
        var retrievability = Math.Clamp(r, 0.0, 1.0);

        var failed = W[11]
                     * Math.Pow(d, -W[12])
                     * (Math.Pow(stability + 1.0, W[13]) - 1.0)
                     * Math.Exp(W[14] * (1.0 - retrievability));

        return Math.Clamp(Math.Min(failed, stability), MinStability, stability);
    }

    /// **Massed practice: why there is no same-day branch, and why the obvious one is a trap.**
    ///
    /// CANON §3.3 gives a short-term formula — `S' = S · e^{w17·(G − 3 + w18)}` — and says of it:
    /// *"near-flat, because massed practice buys almost no stability. **The model refuses to reward
    /// cramming.** This is not a policy we added; it falls out of the memory model."*
    ///
    /// **The conclusion is right. The formula it cites is the one thing that would have broken it.**
    ///
    /// ## What I measured
    ///
    /// That formula is not near-flat. With the published weights a same-day `Good` multiplies
    /// stability by `e^(0.51655 × 0.6621) ≈ 1.41`. Once, that looks harmless. It **compounds** — and
    /// a per-call cap cannot bound a whole day: I implemented the formula, clamped every single call
    /// to at most 1.05×, and twenty reps in one evening *still* came out at **2.65×**.
    ///
    /// ## What actually delivers the refusal
    ///
    /// **Retrievability, which was doing the job all along.**
    ///
    /// Reps twenty minutes apart leave `R ≈ 0.999`. The desirable-difficulty term
    /// `e^(w10·(1−R)) − 1` therefore collapses to about `0.0005`, and each rep buys roughly **1.3%**.
    /// An entire evening of hammering the same card compounds to **+29%**. Three properly spaced
    /// reps — each landing where the memory has faded to 0.90 — produce a memory **more than twenty
    /// times more durable.** Measured, not asserted: see
    /// `Twenty_reps_in_one_evening_lose_to_three_properly_spaced_ones`.
    ///
    /// So the refusal genuinely *does* fall out of the memory model, exactly as CANON claims. It
    /// falls out of the **ordinary update, applied without exception**. The moment you add a same-day
    /// special case, you break the thing the special case was meant to protect.
    ///
    /// **The fix was to delete code, not to add a policy.** There is no `SameDayCap`, no threshold
    /// and no branch — because each of those would be a lever somebody could later move, and the
    /// whole point is that nobody has to remember to hold this line.
    ///
    /// This method exists only to keep the explanation next to the decision. The next person to read
    /// CANON §3.3 will reach for that short-term formula, exactly as I did.
    private static void MassedPractice()
    {
        // Deliberately empty. See above.
    }

    /// **Difficulty update**, with linear damping and mean reversion.
    ///
    /// `ΔD = −w₆·(G − 3)` · `D' = D + ΔD·(10 − D)/9` · `D'' = w₇·D₀(Easy) + (1 − w₇)·D'`
    ///
    /// The damping makes it **hard to push difficulty to the extremes** — one catastrophic evening
    /// should not permanently mark a concept as impossible for somebody, and one lucky guess should
    /// not mark it trivial.
    public static double NextDifficulty(double difficulty, Grade grade)
    {
        var d = Math.Clamp(difficulty, 1.0, 10.0);

        var delta = -W[6] * ((int)grade - 3);

        var damped = d + (delta * (10.0 - d) / 9.0);

        var reverted = (W[7] * InitialDifficulty(Grade.Easy)) + ((1.0 - W[7]) * damped);

        return Math.Clamp(reverted, 1.0, 10.0);
    }

    /// **Belief decays towards the prior, not towards zero.**
    ///
    /// A forgotten concept does not leave you knowing *less than a stranger*. It leaves you knowing
    /// roughly what a stranger knows — which, for a well-designed multiple-choice item, is not
    /// nothing, and for a build task is close to it.
    ///
    /// Decaying to zero would also have the Twin claim a certainty it has not earned: *"this person
    /// definitely cannot do this"*, said of somebody who did it perfectly a year ago and has simply
    /// not been asked since.
    public static double DecayBelief(double pKnows, double prior, double stability, double daysElapsed)
    {
        var retained = Retrievability(stability, daysElapsed);

        return prior + ((pKnows - prior) * retained);
    }
}

/// What the learner just did, on the FSRS scale.
///
/// Note that this is **not** a self-report of how it felt. PRAXIS derives it from what actually
/// happened — whether they got it right, how long they took, and whether they needed help — because
/// a learner asked *"was that easy?"* is being asked to grade themselves, and people are famously
/// bad at that in a very specific direction (C-6).
public enum Grade
{
    /// **They could not retrieve it.** A lapse.
    Again = 1,

    /// Right, but it was a struggle — slow, or hesitant, or hard-won.
    Hard = 2,

    /// Right, at the pace you would expect. The ordinary case.
    Good = 3,

    /// **Right, instantly, effortlessly.** Which is *worth less as evidence than it feels*, because
    /// it means the memory was fresh and nothing was reconsolidated.
    Easy = 4,
}

/// Where one learner's memory of one concept stands.
///
/// `Difficulty` without `Stability` is meaningless and `Stability` without `Difficulty` is
/// unupdatable, so they travel together and there is no way to have one without the other.
public sealed record MemoryState(double Stability, double Difficulty)
{
    /// Nothing has ever been retrieved. **Not "they know nothing"** — nothing has been *observed*, and
    /// those are different claims.
    public static MemoryState New { get; } = new(0.0, 0.0);

    public bool IsNew => Stability <= 0.0;

    /// When this falls to the target retention, it is due.
    public double Retrievability(double daysElapsed)
        => Fsrs.Retrievability(Stability, daysElapsed);

    /// How long PRAXIS believes this will last before it needs seeing again.
    public double IntervalFor(double targetRetention)
        => Fsrs.Interval(Stability, targetRetention);
}
