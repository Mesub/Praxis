namespace Praxis.Modules.Analytics.Domain;

/// **The one number PRAXIS is judged by — and the one it refuses to flatter (I-10).**
///
/// CANON: `Mastery-per-Minute = Δ(verified, decay-adjusted Mastery Credit) / minutes invested`. It is
/// a *rate of return on your attention*, and it is deliberately the metric a product optimising for
/// engagement can never show, because engagement makes the denominator go **up** and this number go
/// **down**. A product that keeps you on the app longer for the same learning has a lower MPM, and
/// PRAXIS reports that against itself.
///
/// Two refusals live in the arithmetic:
///
/// - **No minutes, no rate.** A rate over zero time is not "zero mastery per minute" and it is not
///   infinity — it is *undefined*, and PRAXIS says so rather than inventing a number. A learner who
///   has done nothing has not "gained nothing per minute"; there is simply nothing to divide yet.
/// - **Gaining nothing is a real, reportable outcome.** When minutes were spent and verified mastery
///   did not move, the rate is a true, honest **zero** — and PRAXIS is willing to say *"you gained
///   nothing this week"* out loud, because a metric that cannot report a bad week is a vanity metric.
///
/// This is pure so the whole thing is one testable function of two numbers — the same discipline that
/// let the memory model be swept in a unit test rather than asserted by taste.
public static class MasteryPerMinute
{
    /// Δ mastery credit over minutes. Over a learner's whole history, Δ is simply the mastery credit
    /// they now hold, because they started at zero — so the caller passes the *gain* over whatever
    /// window it measured, and the honest lifetime case passes today's total.
    public static Mpm Compute(double masteryCreditGained, double minutesInvested)
    {
        if (minutesInvested <= 0.0)
        {
            // Undefined, not zero. There is nothing to divide.
            return new Mpm(Rate: null, MasteryCreditGained: Math.Max(0.0, masteryCreditGained), MinutesInvested: 0.0);
        }

        // Mastery credit does not go negative on this axis: decay is already inside the credit the
        // caller measured, and a *loss* is reported as a gain of zero, not a negative rate — the
        // question is "what did your minutes buy", and the answer is never less than nothing.
        var gained = Math.Max(0.0, masteryCreditGained);

        return new Mpm(Rate: gained / minutesInvested, MasteryCreditGained: gained, MinutesInvested: minutesInvested);
    }
}

/// The result, with enough structure that the screen can be honest without recomputing anything.
public sealed record Mpm(double? Rate, double MasteryCreditGained, double MinutesInvested)
{
    /// True once any time has been invested — below this there is no rate to report, only an invitation.
    public bool HasData => MinutesInvested > 0.0;

    /// True when time was spent and verified mastery did not move. The sentence PRAXIS will say.
    public bool GainedNothing => MinutesInvested > 0.0 && MasteryCreditGained <= 0.0;
}
