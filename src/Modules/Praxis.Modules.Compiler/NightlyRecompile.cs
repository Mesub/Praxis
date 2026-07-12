using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Compiler;

/// **The nightly pass. What is it actually for?**
///
/// Not for generating plans. A plan is a pure function of the learner and is computed on demand
/// (see `PlanService`), so a job that precomputed them would be caching an answer that is cheaper to
/// recompute than to fetch — and would then serve a stale one the first time someone proved
/// something at 9am.
///
/// It is for the thing **no request will ever ask about, and no event will ever announce**:
///
/// > *A learner who was on track yesterday is not on track today, and nobody has opened the app.*
///
/// Everything they hold decayed a little overnight. A concept that was fresh enough to build on is
/// now not. The plan got longer while they slept, and the Decay Wall verdict may have flipped from
/// *"yes, twelve months"* to *"no — at your pace you will hold half of it"*.
///
/// **That is news, and it should reach them before they waste another week.** It is also precisely
/// the news a product optimising for engagement would never send, because it invites them to leave.
///
/// The job's output is therefore not a cached plan. It is a **change of verdict** — and, in Phase 1,
/// the Notification Engine's cue to say something true and unwelcome.
public sealed partial class NightlyRecompile : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly ILogger<NightlyRecompile> _logger;

    public NightlyRecompile(
        IServiceScopeFactory scopes,
        IClock clock,
        ILogger<NightlyRecompile> logger)
        => (_scopes, _clock, _logger) = (scopes, clock, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A background service that dies on one learner's bad data stops
            catch (Exception ex)   // recompiling everyone else's plan. Log it and keep going.
#pragma warning restore CA1031
            {
                Failed(_logger, ex);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// Exposed so a test — and an operator — can run the pass without waiting until 3am.
    public async Task<NightlyResult> RunOnceAsync(CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<CompilerDbContext>();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();

        var now = _clock.UtcNow;

        var stale = await db.Objectives
            .Where(o => o.IsActive)
            .ToListAsync(ct);

        var recompiled = 0;
        var fellBehind = new List<Guid>();

        foreach (var objective in stale)
        {
            if (!RecompileTrigger.IsStaleByAge(objective.LastCompiledAt, now))
            {
                continue;   // something already recompiled it today; nothing has changed since
            }

            var result = await plans.CompileAsync(objective, ct);

            if (!result.IsSuccess)
            {
                continue;
            }

            recompiled++;

            if (!result.Value.Believable)
            {
                // **The news nobody else would send.** Overnight, this learner's objective slipped
                // out of reach at the pace they committed to — and they have no idea, because
                // nothing happened. Something *not happening* is exactly the problem.
                fellBehind.Add(objective.LearnerId);

                FellBehind(
                    _logger,
                    objective.LearnerId,
                    result.Value.Wall.ConceptsReached,
                    result.Value.Wall.ConceptsInPlan,
                    result.Value.Alternative?.MinutesPerDayThatWouldWork ?? 0,
                    objective.MinutesPerDay);
            }
        }

        Completed(_logger, recompiled, fellBehind.Count);

        return new NightlyResult(recompiled, fellBehind);
    }

    /// Hourly, not daily. "Nightly" is a lie in a product with learners in every timezone — the pass
    /// wakes often and does nothing unless a plan is genuinely older than its shelf life.
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information,
        Message = "Recompiled {Recompiled} plan(s). {FellBehind} learner(s) are no longer on track.")]
    private static partial void Completed(ILogger logger, int recompiled, int fellBehind);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Learner {LearnerId} has fallen behind: reachable {Reached}/{Total} concepts at "
                  + "{CurrentMinutes} min/day. Would need {NeededMinutes} min/day.")]
    private static partial void FellBehind(
        ILogger logger, Guid learnerId, int reached, int total, int neededMinutes, int currentMinutes);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error,
        Message = "The nightly recompile failed. Plans will be stale until it next runs.")]
    private static partial void Failed(ILogger logger, Exception ex);
}

public sealed record NightlyResult(
    int Recompiled,

    /// Learners whose objective slipped out of reach overnight, without them doing anything wrong —
    /// which is the point. They should be told.
    IReadOnlyList<Guid> FellBehind);
