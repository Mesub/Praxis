using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Learning;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Features.States;

/// **Decay applied at read time.**
///
/// The projection records what the learner proved and when. It does *not* record what they know
/// today, because that changes continuously and nobody is writing to the database while they sleep.
///
/// So the rot is computed here, on the way out. Refresh an hour later and the number is lower — as
/// it should be, because an hour of forgetting actually happened.
///
/// This is the one place in the Twin that reads a clock, and it is safe precisely because it writes
/// nothing. The stored state stays a pure function of the event log; only the *view* of it moves
/// with time.
public static class LiveTwin
{
    /// The retrievability at which a concept is considered due for review. Below this, it is
    /// slipping away faster than the learner realises.
    public const double DueThreshold = 0.90;

    /// Below this, treat it as lapsed: they almost certainly could not produce it today, whatever
    /// the Ledger says they once could.
    public const double LapsedThreshold = 0.70;

    public static ConceptStateResponse Project(
        ConceptState state,
        IReadOnlyList<Misconception> misconceptions,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(misconceptions);

        var elapsed = state.LastEvidenceAt is null
            ? 0.0
            : Math.Max(0.0, (now - state.LastEvidenceAt.Value).TotalDays);

        var retrievability = state.Stability > 0
            ? Fsrs.Retrievability(state.Stability, elapsed)
            : 0.0;

        var pKnowsNow = Fsrs.DecayBelief(
            state.PKnows, ConceptState.PriorPKnows, state.Stability, elapsed);

        // Null, not infinity. There is no due date for something never proved — and infinity is
        // both a lie and unserialisable, which is how this surfaced: as a 500 on a learner's very
        // first look at a concept they had not touched.
        double? daysUntilDue = state.NextDueAt is null
            ? null
            : (state.NextDueAt.Value - now).TotalDays;

        var active = misconceptions
            .Where(m => m.IsActive)
            .Select(m => new MisconceptionResponse(
                m.Key,
                m.Statement,
                m.TimesObserved,
                HasRelapsed: m.TimesContradicted == 0 && m.TimesObserved > 1))
            .ToList();

        return new ConceptStateResponse(
            ConceptId: state.ConceptId,
            PKnows: pKnowsNow,
            PKnowsAtLastEvidence: state.PKnows,
            MasteryCredit: state.MasteryCredit,
            IsHeld: state.IsHeld,
            Retrievability: retrievability,
            DaysUntilDue: daysUntilDue,
            LastEvidenceAt: state.LastEvidenceAt,
            NextDueAt: state.NextDueAt,
            ConfidenceGap: state.ConfidenceGap,
            BrierScore: state.BrierScore,
            CalibrationIsMeaningful: Calibration.IsMeaningful(state.CalibrationSamples),
            Assurance: state.Assurance,
            EvidenceCount: state.EvidenceCount,
            ActiveMisconceptions: active);
    }

    /// **Held, but rotting.** Above the threshold today and heading below it — the state nobody else
    /// in this industry will show you, because it is the state that makes a certificate a lie.
    public static bool IsDecaying(ConceptStateResponse s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s.IsHeld && s.Retrievability < DueThreshold && s.Retrievability >= LapsedThreshold;
    }

    /// Already gone. The Ledger says they proved it; today they probably could not.
    public static bool HasLapsed(ConceptStateResponse s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s.IsHeld && s.Retrievability < LapsedThreshold;
    }
}
