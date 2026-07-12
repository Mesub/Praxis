using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Features.States;

/// **The learner's own mind, and it is willing to tell them the truth about it.**
///
/// Including the number nobody else shows you: how much of what they "know" is currently rotting.
public static class GetTwin
{
    public record Query(int ForecastDays = 30);

    public sealed class Handler
    {
        private readonly TwinDbContext _db;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(TwinDbContext db, IUserContext user, IClock clock)
            => (_db, _user, _clock) = (db, user, clock);

        public async Task<Result<TwinSummaryResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();
            var now = _clock.UtcNow;

            var states = await _db.States
                .AsNoTracking()
                .Where(s => s.LearnerId == learner)
                .ToListAsync(ct);

            var misconceptions = await _db.Misconceptions
                .AsNoTracking()
                .Where(m => m.LearnerId == learner)
                .ToListAsync(ct);

            var byConcept = misconceptions
                .GroupBy(m => m.ConceptId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var live = states
                .Select(s => LiveTwin.Project(
                    s, byConcept.GetValueOrDefault(s.ConceptId, []), now))
                .ToList();

            var graded = states.Where(s => s.CalibrationSamples > 0).ToList();

            // Weighted by how many items each concept was graded on — a concept with 40 responses
            // says more about a learner's calibration than one with two.
            var samples = graded.Sum(s => s.CalibrationSamples);

            var brier = samples == 0
                ? 0.0
                : graded.Sum(s => s.BrierScore * s.CalibrationSamples) / samples;

            var gap = samples == 0
                ? 0.0
                : graded.Sum(s => s.ConfidenceGap * s.CalibrationSamples) / samples;

            var forecast = live
                .Where(s => s.IsHeld && s.DaysUntilDue is { } d && d <= query.ForecastDays)
                .OrderBy(s => s.DaysUntilDue)
                .Select(s => new DecayForecastEntry(
                    s.ConceptId,
                    s.PKnows,
                    s.Retrievability,
                    s.DaysUntilDue!.Value,
                    AlreadyLapsed: LiveTwin.HasLapsed(s)))
                .ToList();

            return Result<TwinSummaryResponse>.Success(new TwinSummaryResponse(
                ConceptsHeld: live.Count(s => s.IsHeld),
                ConceptsInProgress: live.Count(s => !s.IsHeld && s.EvidenceCount > 0),
                ConceptsDecaying: live.Count(LiveTwin.IsDecaying),
                ConceptsLapsed: live.Count(LiveTwin.HasLapsed),
                OverallBrierScore: brier,
                OverallConfidenceGap: gap,
                CalibrationIsMeaningful: Calibration.IsMeaningful(samples),
                ActiveMisconceptions: misconceptions.Count(m => m.IsActive),
                DecayForecast: forecast));
        }
    }
}
