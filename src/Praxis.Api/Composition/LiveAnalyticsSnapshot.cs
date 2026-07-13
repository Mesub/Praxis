using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Analytics;
using Praxis.Modules.Twin.Persistence;

namespace Praxis.Api.Composition;

/// **The numerator of Mastery-per-Minute, read from the Twin.**
///
/// The Twin believes; Analytics divides. Neither reads the other's tables, so the adapter lives here.
/// It sums the Twin's **decay-adjusted** Mastery Credit across the learner's concepts — real,
/// evidence-backed learning that has already been discounted for forgetting, so a competency the
/// learner has let rot contributes less, exactly as the honest number should.
public sealed class LiveAnalyticsSnapshot : IAnalyticsSnapshot
{
    private readonly TwinDbContext _twin;

    public LiveAnalyticsSnapshot(TwinDbContext twin) => _twin = twin;

    public async Task<double> MasteryCreditByAsync(Guid learnerId, CancellationToken ct = default)
        => await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId)
            .SumAsync(s => s.MasteryCredit, ct);
}
