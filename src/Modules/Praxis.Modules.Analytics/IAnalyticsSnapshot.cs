using Praxis.Modules.Analytics.Domain;

namespace Praxis.Modules.Analytics;

/// **What the learner has actually verified** — the numerator of Mastery-per-Minute, read from the
/// Twin at the composition root because no module reads another's tables. It is the sum of the Twin's
/// decay-adjusted Mastery Credit: real, evidence-backed learning, already discounted for forgetting.
public interface IAnalyticsSnapshot
{
    Task<double> MasteryCreditByAsync(Guid learnerId, CancellationToken ct = default);

    /// Each concept the learner currently **holds**, with its forgetting-curve retrievability computed
    /// at read time. Analytics aggregates these into decay debt; the Twin owns the curve. A learner
    /// who holds nothing returns an empty list, and owes nothing.
    Task<IReadOnlyList<ConceptDecay>> HeldConceptDecayAsync(
        Guid learnerId, CancellationToken ct = default);
}
