namespace Praxis.Modules.Memory.Domain;

/// **What is due now, what is overdue, and what is about to rot.**
///
/// Every spaced-repetition product in the world builds this queue the same way: sort by how overdue
/// each card is, put the most neglected one on top, and show the learner a red badge with the count.
///
/// **That ordering puts your least valuable item first, and I can show you the arithmetic.**
///
/// ## What a rep is actually worth
///
/// Repping something has two outcomes. You get it right — a cheap three-minute review, and the
/// memory consolidates hard because you had to reach for it. Or you get it wrong — which is **not a
/// review at all.** It is a lapse: stability collapses, and the learner spends fifteen minutes
/// relearning something they thought they knew.
///
/// So the honest value of repping a concept today is the *expected* gain divided by the *expected*
/// cost:
///
/// ```
///   E[ΔS]     =  R · (S_recall − S)  +  (1 − R) · (S_lapse − S)
///   E[minutes] =  R · 3              +  (1 − R) · 15
/// ```
///
/// ## And it has an interior peak, which nobody's queue accounts for
///
/// Measured across the model: value-per-minute **peaks at R ≈ 0.63**, and the peak sits in the same
/// place whether the memory has five days of stability, twenty, or a hundred and twenty. It is a
/// property of the curve, not a number anybody chose.
///
/// | Retrievability | worth (stability-days per minute, S = 20) |
/// |---|---|
/// | 0.95 — barely faded | **5.2** |
/// | 0.63 — the sweet spot | **14.5** |
/// | 0.10 — long gone | **4.2** |
///
/// Read the last row. **Something you have almost entirely forgotten is worth *less* than something
/// you have barely forgotten** — because you will probably fail it, and a failed rep costs five times
/// as much and gives back a collapsed memory.
///
/// So the naive queue is not merely suboptimal. It is **inverted at the top**: it hands the learner
/// their single worst-value item and calls it the most urgent thing they own.
///
/// ## The refusal
///
/// **PRAXIS will not show you a review queue containing things you have already lost.**
///
/// Below `NotAReviewBelow`, the *single most likely outcome* of putting a concept in a three-minute
/// review queue is that you fail it and spend fifteen minutes relearning it. A queue that prices
/// those at three minutes is lying to you about your own evening — and it is the lie that turns a
/// study tool into a wall of four hundred red cards that people quietly stop opening.
///
/// Those concepts are not hidden. They are named, counted, and handed **back to the plan** as
/// re-learns, at the price a re-learn actually costs.
public static class Reps
{
    /// **Derived, not chosen.** Value-per-minute peaks here, and `TheSweetSpotIsWhereTheModelSaysItIs`
    /// re-derives it from the curve every build — so if the FSRS weights are ever refitted and the
    /// peak moves, the test fails rather than this constant quietly becoming folklore.
    public const double PeakRetrievability = 0.63;

    /// A review you pass.
    public const double RepMinutes = 3.0;

    /// A review you **fail**, which is not a review. Stability collapses and the learner is
    /// relearning something they believed they knew — and that is where the five-fold cost lives.
    public const double RelearnMinutes = 15.0;

    /// Above this, a rep buys almost nothing: the memory is fresh, nothing was reconsolidated, and
    /// the minute is better spent elsewhere. **Being "due" early is not a virtue.**
    public const double NotYetWorthItAbove = 0.90;

    /// **Below this you are more likely to fail it than to pass it — so it is not a review.**
    ///
    /// This is the honest line, and it is drawn where the coin flips rather than where it looked
    /// tidy. Calling a coin-toss-you-lose a "3-minute rep" is not an optimisation error, it is a
    /// false statement about how the learner's evening is going to go.
    public const double NotAReviewBelow = 0.50;

    /// **What repping this today is actually worth**, in stability-days earned per minute spent.
    ///
    /// Expected over both outcomes — because a queue that prices every rep as though the learner will
    /// pass it has priced the one thing that matters at zero.
    public static double ValuePerMinute(MemoryState state, double daysElapsed)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsNew)
        {
            // Never proved. It is not due for review — it is not learned yet, and that is the plan's
            // business, not the queue's.
            return 0.0;
        }

        var r = Fsrs.Retrievability(state.Stability, daysElapsed);

        var onRecall = Fsrs.AfterRecall(state.Stability, state.Difficulty, r, Grade.Good);
        var onLapse = Fsrs.AfterLapse(state.Stability, state.Difficulty, r);

        var expectedGain = ((r * onRecall) + ((1.0 - r) * onLapse)) - state.Stability;

        var expectedMinutes = ExpectedMinutes(r);

        return expectedGain / expectedMinutes;
    }

    /// **What this rep will actually cost**, not what the queue would like to claim it costs.
    public static double ExpectedMinutes(double retrievability)
    {
        var r = Math.Clamp(retrievability, 0.0, 1.0);

        return (r * RepMinutes) + ((1.0 - r) * RelearnMinutes);
    }

    public static RepUrgency Classify(MemoryState state, double daysElapsed)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsNew)
        {
            return RepUrgency.NeverLearned;
        }

        var r = Fsrs.Retrievability(state.Stability, daysElapsed);

        if (r < NotAReviewBelow)
        {
            return RepUrgency.Lost;
        }

        if (r > NotYetWorthItAbove)
        {
            return RepUrgency.Fresh;
        }

        // Past the peak and falling: every day from here, the rep is worth less and the odds of
        // failing it get worse. **This is the one to do today.**
        return r < PeakRetrievability ? RepUrgency.Rotting : RepUrgency.Ripe;
    }

    /// **The queue, ordered by what it is worth — not by what you owe.**
    ///
    /// There is no count of neglect here, no streak, and no red badge. A learner opening this sees the
    /// thing most worth ten minutes of their life, and PRAXIS's honest opinion of what everything else
    /// would cost.
    public static RepPlan Build(IReadOnlyList<DueRep> reps)
    {
        ArgumentNullException.ThrowIfNull(reps);

        var worth = new List<ScoredRep>();
        var lost = new List<ScoredRep>();

        foreach (var rep in reps)
        {
            var urgency = Classify(rep.State, rep.DaysSinceReview);

            if (urgency is RepUrgency.NeverLearned)
            {
                continue;
            }

            var r = Fsrs.Retrievability(rep.State.Stability, rep.DaysSinceReview);

            var scored = new ScoredRep(
                rep.ConceptId,
                rep.Slug,
                Retrievability: r,
                Urgency: urgency,
                ValuePerMinute: ValuePerMinute(rep.State, rep.DaysSinceReview),
                ExpectedMinutes: ExpectedMinutes(r));

            // **Lost concepts do not go in the review queue.** They are not hidden either — they go
            // back to the plan, priced as the re-learns they are.
            if (urgency is RepUrgency.Lost)
            {
                lost.Add(scored);
            }
            else if (urgency is not RepUrgency.Fresh)
            {
                worth.Add(scored);
            }
        }

        return new RepPlan(
            // Highest value first. **Not most overdue first** — that ordering hands the learner their
            // worst-value item and calls it the most urgent thing they own.
            Due: [.. worth
                .OrderByDescending(s => s.ValuePerMinute)
                .ThenBy(s => s.ConceptId)],
            Lost: [.. lost.OrderBy(s => s.Retrievability).ThenBy(s => s.ConceptId)]);
    }
}

/// One concept the learner holds, and how long since they last proved they still did.
public sealed record DueRep(Guid ConceptId, string Slug, MemoryState State, double DaysSinceReview);

/// A rep, priced honestly.
public sealed record ScoredRep(
    Guid ConceptId,
    string Slug,
    double Retrievability,
    RepUrgency Urgency,

    /// Stability-days earned per minute spent. **The only number that should ever sort this queue.**
    double ValuePerMinute,

    /// What it will *actually* take, averaged over passing and failing it.
    double ExpectedMinutes);

/// **The queue, and the honest bill.**
public sealed record RepPlan(IReadOnlyList<ScoredRep> Due, IReadOnlyList<ScoredRep> Lost)
{
    /// What clearing the queue would really cost — expected, not nominal. **A queue that quotes you
    /// three minutes a card and then hands you fifteen is not a schedule, it is a trap.**
    public double ExpectedMinutes => Due.Sum(d => d.ExpectedMinutes);

    /// Past the peak and falling. Every day these wait, they are worth less and likelier to fail.
    public IReadOnlyList<ScoredRep> Rotting
        => [.. Due.Where(d => d.Urgency is RepUrgency.Rotting)];

    /// **What PRAXIS says when there is nothing worth doing.**
    ///
    /// Not *"you have 0 cards due, keep your streak alive!"* — nothing. The point was never the app
    /// (I-10).
    public bool NothingWorthDoing => Due.Count == 0;

    /// The honest sentence about what has been lost — said once, plainly, and **never as a badge, a
    /// counter, or a red dot.**
    ///
    /// PRAXIS tells a learner they have lost something the way a colleague would: once, clearly, with
    /// what it will cost to fix. Not by decorating their home screen with it every morning until they
    /// uninstall.
    public string? AboutWhatYouLost
        => Lost.Count == 0
            ? null
            : $"{Lost.Count} concept(s) have decayed past the point where a review would work. "
              + "PRAXIS is not going to put them in your review list wearing a three-minute price "
              + "tag — you would fail them, and it would cost you an evening you had not planned "
              + "for. They go back to the plan as re-learns, priced honestly.";
}

/// How urgent a rep is, and — more usefully — **whether it is a rep at all.**
public enum RepUrgency
{
    /// Never proved. **Not "due"** — not learned yet. That is the plan's business, not the queue's.
    NeverLearned = 0,

    /// Still sharp. Repping it now buys almost nothing (M8.1), and **being early is not a virtue.**
    Fresh = 1,

    /// **In the window.** Faded enough that retrieving it will bite, sure enough that they will
    /// manage it. The best minutes in the product.
    Ripe = 2,

    /// **Past the peak, and falling.** Every day this waits it is worth less and likelier to fail.
    /// This is the one to do today.
    Rotting = 3,

    /// **Gone.** More likely to be failed than passed — so calling it a "review" is a false statement
    /// about how the learner's evening will go. It is a re-learn, and it is priced as one.
    Lost = 4,
}
