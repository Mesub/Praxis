using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Projections;

/// Drives projections forward through the log, and — when asked — throws them away and rebuilds
/// them from nothing.
///
/// The rebuild is not a maintenance tool. It is the proof that the log is the source of truth:
/// if the Twin cannot be reconstructed from history alone, then some belief about a learner is
/// being held somewhere it can never be audited, and the Ledger's claims become unverifiable.
/// M1.6 tests exactly that.
public sealed partial class ProjectionRunner
{
    private readonly IEventStore _store;
    private readonly KernelDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ProjectionRunner> _logger;
    private readonly IReadOnlyList<IProjection> _projections;

    public ProjectionRunner(
        IEventStore store,
        KernelDbContext db,
        IClock clock,
        ILogger<ProjectionRunner> logger,
        IEnumerable<IProjection> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);

        _store = store;
        _db = db;
        _clock = clock;
        _logger = logger;
        _projections = [.. projections];

        var duplicate = _projections
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two projections are both called '{duplicate.Key}'. The name is the checkpoint "
                + "key — sharing it means they would overwrite each other's place in history.");
        }
    }

    /// Bring every projection up to the end of the log.
    public async Task<ProjectionRunResult> CatchUpAsync(CancellationToken ct = default)
    {
        var applied = 0;

        foreach (var projection in _projections)
        {
            applied += await CatchUpAsync(projection, ct);
        }

        return new ProjectionRunResult(_projections.Count, applied);
    }

    /// Throw every projection away and replay the entire log into it.
    ///
    /// Everything a projection holds is destroyed first. Nothing in a projection may be
    /// irreplaceable, because this is allowed to happen at any time.
    public async Task<ProjectionRunResult> RebuildAsync(CancellationToken ct = default)
    {
        RebuildStarting(_logger, _projections.Count);

        foreach (var projection in _projections)
        {
            await projection.ResetAsync(ct);

            var checkpoint = await CheckpointFor(projection.Name, ct);
            checkpoint.Rewind(_clock.UtcNow);
        }

        await _db.SaveChangesAsync(ct);

        var result = await CatchUpAsync(ct);

        RebuildFinished(_logger, result.ProjectionCount, result.EventsApplied);

        return result;
    }

    private async Task<int> CatchUpAsync(IProjection projection, CancellationToken ct)
    {
        var checkpoint = await CheckpointFor(projection.Name, ct);

        var applied = 0;
        var lastSeen = checkpoint.LastSequence;

        await foreach (var e in _store.ReadAllFromAsync(checkpoint.LastSequence, ct: ct))
        {
            // The checkpoint advances past events this projection ignores. Otherwise it would
            // re-read the whole tail of the log on every run, forever.
            lastSeen = e.Sequence;

            if (!projection.Handles.Contains(e.Type))
            {
                continue;
            }

            await projection.ApplyAsync(e, ct);
            applied++;
        }

        if (lastSeen != checkpoint.LastSequence)
        {
            checkpoint.AdvanceTo(lastSeen, _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
        }

        return applied;
    }

    private async Task<ProjectionCheckpoint> CheckpointFor(string name, CancellationToken ct)
    {
        var existing = await _db.Checkpoints
            .FirstOrDefaultAsync(c => c.ProjectionName == name, ct);

        if (existing is not null)
        {
            return existing;
        }

        var fresh = ProjectionCheckpoint.StartOf(name, _clock.UtcNow);
        _db.Checkpoints.Add(fresh);

        return fresh;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning,
        Message = "Rebuilding {ProjectionCount} projection(s) from zero. Everything they hold is being destroyed and recomputed from the event log.")]
    private static partial void RebuildStarting(ILogger logger, int projectionCount);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Rebuilt {ProjectionCount} projection(s) from {EventsApplied} event(s).")]
    private static partial void RebuildFinished(ILogger logger, int projectionCount, int eventsApplied);
}

public sealed record ProjectionRunResult(int ProjectionCount, int EventsApplied);
