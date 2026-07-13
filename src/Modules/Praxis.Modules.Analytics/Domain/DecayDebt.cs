namespace Praxis.Modules.Analytics.Domain;

/// **How much of what you proved has quietly slipped away, and is owed back to review.**
///
/// The other analytics folds describe effort and cadence. This one describes the *interest on
/// forgetting*. Every competency a learner holds sits on a forgetting curve, and unless it is
/// refreshed its retrievability bleeds down continuously. Decay debt is the honest total of that
/// bleed: for each held concept, the share of its mastery credit that is no longer retrievable right
/// now. It is the number nobody else in the industry shows, because a course that admitted your
/// certificate was rotting could not sell you the certificate.
///
/// ## Where the numbers come from — and why this stays clean
///
/// The forgetting curve is the Twin's, computed at read time from FSRS stability and the wall clock
/// (the one place decay looks at "now"). Analytics does **not** re-derive it and does **not** read the
/// Twin's tables — it receives the already-decayed per-concept figures through `IAnalyticsSnapshot`
/// and only *aggregates* them here, purely, so this fold is a total testable in isolation.
///
/// ## The invariant that keeps it honest
///
/// `CreditAtRisk + RetainedCredit == the total held credit`. The debt is not a separate scary number
/// bolted on; it is exactly the part of what you already earned that you can no longer reach. Split,
/// never invented.
public static class DecayDebt
{
    public static DecayDebtReport From(IReadOnlyList<ConceptDecay> heldConcepts)
    {
        ArgumentNullException.ThrowIfNull(heldConcepts);

        var atRisk = 0.0;
        var retained = 0.0;
        var decaying = 0;
        var lapsed = 0;

        foreach (var c in heldConcepts)
        {
            var retrievability = Math.Clamp(c.Retrievability, 0.0, 1.0);

            // The share of this competency you can still reach, and the share that has bled away.
            // They sum to the whole credit — the debt is a split of what you earned, not an addition.
            retained += c.MasteryCredit * retrievability;
            atRisk += c.MasteryCredit * (1.0 - retrievability);

            if (c.HasLapsed)
            {
                lapsed++;
            }
            else if (c.IsDecaying)
            {
                decaying++;
            }
        }

        return new DecayDebtReport(
            CreditAtRisk: atRisk,
            RetainedCredit: retained,
            ConceptsDecaying: decaying,
            ConceptsLapsed: lapsed,
            HasHeldConcepts: heldConcepts.Count > 0);
    }
}

/// One held concept's decay, as the Twin sees it right now. `Retrievability` is the forgetting-curve
/// value at read time (1.0 the moment it was proved); the flags mirror the Twin's own thresholds —
/// `IsDecaying` is held-but-slipping, `HasLapsed` is already fallen below.
public sealed record ConceptDecay(
    double MasteryCredit,
    double Retrievability,
    bool IsDecaying,
    bool HasLapsed);

/// The debt, split from what is retained, with the counts that make it actionable. `HasHeldConcepts`
/// is false for a learner who has proved nothing yet — there is no debt to speak of, and the screen
/// says nothing rather than a reassuring zero.
public sealed record DecayDebtReport(
    double CreditAtRisk,
    double RetainedCredit,
    int ConceptsDecaying,
    int ConceptsLapsed,
    bool HasHeldConcepts)
{
    public static readonly DecayDebtReport None = new(0.0, 0.0, 0, 0, false);

    /// True when some measurable mastery has actually slipped. A learner whose every concept is fresh
    /// holds credit but owes nothing.
    public bool HasDebt => HasHeldConcepts && CreditAtRisk > 0.0;
}
