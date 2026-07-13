namespace Praxis.Modules.Analytics;

/// **What the learner has actually verified** — the numerator of Mastery-per-Minute, read from the
/// Twin at the composition root because no module reads another's tables. It is the sum of the Twin's
/// decay-adjusted Mastery Credit: real, evidence-backed learning, already discounted for forgetting.
public interface IAnalyticsSnapshot
{
    Task<double> MasteryCreditByAsync(Guid learnerId, CancellationToken ct = default);
}
