namespace Praxis.Modules.Reality.Domain;

/// **A Loop you let expire is not deleted. It is scored — slightly worse than doing nothing** (doc §4.7).
///
/// This is the counterweight to the Loops board's Coverage metric (M9.6), and together they close the
/// one gap a prediction market has: **cherry-picking by attrition.** If forfeiting were free, the
/// optimal strategy would be to make a hundred predictions and only ever resolve the ones going your
/// way — a spotless track record built entirely of survivorship. So a forfeit costs something.
///
/// ## But only a little, and that is deliberate
///
/// The penalty is small, because **you are not punished for having a life.** A learner who committed
/// to a two-week market prediction and then had a busy fortnight has not sinned; they have just not
/// produced evidence. The forfeit sits *slightly* worse than the do-nothing baseline — enough that
/// ducking is never the profitable move, never so much that a missed week is a catastrophe.
///
/// ## And it is permanent
///
/// A forfeit does not decay, cannot be retried, and stays counted in Coverage forever. That
/// permanence is the whole point: the record is of what you *committed to and did*, and quietly
/// un-forfeiting a loop after the fact would be exactly the "make the uncomfortable one disappear"
/// move the board exists to prevent.
public static class ForfeitRule
{
    /// How much worse than doing nothing a forfeit scores. **Small on purpose** — a disincentive to
    /// duck, not a punishment for being busy.
    public const double Penalty = 0.05;

    /// Beyond this the penalty would stop being "you are not punished for having a life" and start
    /// being a trap. A guard against a well-meaning future edit that cranks it up.
    public const double MaxDefensiblePenalty = 0.10;

    /// The Brier score a forfeited Loop earns: the baseline (climatology — what a learner who
    /// predicted nothing informative would score) plus the penalty. **A Brier, so lower is better,
    /// and a forfeit is therefore strictly worse than the baseline it is built on.**
    public static double ForfeitBrier(double baselineBrier)
    {
        var baseline = Math.Clamp(baselineBrier, 0.0, 1.0);

        return Math.Clamp(baseline + Penalty, 0.0, 1.0);
    }

    /// **Ducking is never the profitable move.** For any baseline, forfeiting scores worse than
    /// having predicted the baseline — which is the property that makes attrition-based
    /// cherry-picking a losing strategy.
    public static bool IsWorseThanDoingNothing(double baselineBrier)
        => ForfeitBrier(baselineBrier) > Math.Clamp(baselineBrier, 0.0, 1.0);
}
