using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Analytics;
using Praxis.Modules.Analytics.Domain;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Features.States;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **The Analytics engine's window into the Twin.**
///
/// The Twin believes; Analytics divides. Neither reads the other's tables, so the adapter lives here.
/// It sums the Twin's Mastery Credit for the numerator of Mastery-per-Minute, and — for decay debt —
/// applies the Twin's own forgetting curve at read time so Analytics can aggregate what has slipped
/// without ever re-deriving the curve or touching the clock itself.
public sealed class LiveAnalyticsSnapshot : IAnalyticsSnapshot
{
    private readonly TwinDbContext _twin;
    private readonly IClock _clock;

    public LiveAnalyticsSnapshot(TwinDbContext twin, IClock clock)
        => (_twin, _clock) = (twin, clock);

    public async Task<double> MasteryCreditByAsync(Guid learnerId, CancellationToken ct = default)
        => await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId)
            .SumAsync(s => s.MasteryCredit, ct);

    public async Task<IReadOnlyList<ConceptDecay>> HeldConceptDecayAsync(
        Guid learnerId, CancellationToken ct = default)
    {
        // Only concepts the learner actually holds carry debt — you cannot owe review on something
        // you never proved. A concept still in progress is not a rotting competency; it is an
        // unfinished one.
        var held = await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId && s.IsHeld)
            .Select(s => new { s.MasteryCredit, s.Stability, s.LastEvidenceAt })
            .ToListAsync(ct);

        var now = _clock.UtcNow;

        return held
            .Select(s =>
            {
                // The same read-time forgetting curve the Twin applies (M3.3): retrievability decays
                // from 1.0 as the days since the last evidence pile up.
                var elapsed = s.LastEvidenceAt is null
                    ? 0.0
                    : Math.Max(0.0, (now - s.LastEvidenceAt.Value).TotalDays);

                var retrievability = s.Stability > 0
                    ? Fsrs.Retrievability(s.Stability, elapsed)
                    : 0.0;

                // The Twin's own thresholds, so "decaying" and "lapsed" mean the same thing here as
                // on the graph and in the rep queue.
                var isDecaying = retrievability < LiveTwin.DueThreshold
                    && retrievability >= LiveTwin.LapsedThreshold;
                var hasLapsed = retrievability < LiveTwin.LapsedThreshold;

                return new ConceptDecay(s.MasteryCredit, retrievability, isDecaying, hasLapsed);
            })
            .ToList();
    }
}
