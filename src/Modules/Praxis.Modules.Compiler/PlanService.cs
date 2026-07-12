using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Compiler;

/// **Compiling a learner's plan.**
///
/// ## There is no plan table, and that is deliberate
///
/// CANON calls the Plan *"ephemeral, always regenerable"*, and it means it. A plan is not a
/// commitment or a record — it is the current best answer, and it is a **pure function** of
/// (objective, graph, twin, minutes). Storing it would create a second version of the truth that
/// could drift from the first, and every bug in that class is the same bug: someone reads a cached
/// plan and acts on a learner who no longer exists.
///
/// So the plan is computed on demand, every time. It is Dijkstra over a few hundred nodes — cheaper
/// than the database round-trip that would fetch a stale copy of it.
///
/// What *is* stored is the Objective: what they are trying to become, at what pace, and when we last
/// told them where they stood. That last part is the only thing worth persisting, because it is the
/// only thing that is not recomputable.
public sealed class PlanService
{
    private readonly CompilerDbContext _db;
    private readonly ICompilerSnapshot _snapshot;
    private readonly IClock _clock;

    public PlanService(CompilerDbContext db, ICompilerSnapshot snapshot, IClock clock)
        => (_db, _snapshot, _clock) = (db, snapshot, clock);

    /// The learner's plan, as of this instant, together with **whether PRAXIS actually believes in
    /// it** (the Decay Wall, M4.3).
    public async Task<Result<CompiledPlan>> ForLearnerAsync(Guid learnerId, CancellationToken ct = default)
    {
        var objective = await _db.Objectives
            .FirstOrDefaultAsync(o => o.LearnerId == learnerId && o.IsActive, ct);

        if (objective is null)
        {
            return Result<CompiledPlan>.NotFound(
                "This learner has no objective. PRAXIS cannot compile a plan towards nothing — and "
                + "a product that offered one anyway would be selling motion, not progress.");
        }

        return await CompileAsync(objective, ct);
    }

    public async Task<Result<CompiledPlan>> CompileAsync(Objective objective, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objective);

        var concepts = await _snapshot.ForObjectiveAsync(objective.LearnerId, objective.ConceptId, ct);

        if (concepts.Count == 0)
        {
            return Result<CompiledPlan>.NotFound(
                "The objective's concept is not in any loaded graph. Either the pack was never "
                + "loaded, or the objective points at something that no longer exists.");
        }

        // A deadline that has already passed is not a hard plan — it is an impossible one, and the
        // honest answer is not an empty roadmap with a sad face on it.
        if (objective.Deadline is { } deadline && deadline <= _clock.UtcNow)
        {
            return Result<CompiledPlan>.Invalid(
                $"That deadline passed on {deadline:d}. PRAXIS cannot compile a plan into the past. "
                + "Pick a new date and it will tell you honestly whether it believes in it.");
        }

        var horizon = HorizonFor(objective);

        var compiled = CurriculumCompiler.Compile(
            new CompileRequest(objective.ConceptId, concepts, objective.MinutesPerDay, objective.Deadline));

        if (!compiled.IsSuccess)
        {
            return Result<CompiledPlan>.Invalid(compiled.Message ?? "The plan could not be compiled.", compiled.Errors);
        }

        var plan = compiled.Value;

        // ── Do we actually believe in this? ──────────────────────────────────
        //
        // The whole point of the wall. PRAXIS runs the simulation *before* handing anyone a plan,
        // and it is willing to come back and say no.
        var verdict = DecayWall.Simulate(plan, objective.MinutesPerDay, horizon);

        if (verdict.Reachable)
        {
            objective.RecordCompile(_clock.UtcNow, sequence: 0);
            await _db.SaveChangesAsync(ct);

            return Result<CompiledPlan>.Success(
                new CompiledPlan(plan, verdict, Believable: true, Alternative: null));
        }

        // ── We do not. Say so, and offer what we DO believe in ───────────────
        //
        // Not "try harder". A number they can act on, and a narrower objective PRAXIS is willing to
        // stand behind — because offering a fallback you also do not believe in is worse than
        // offering nothing.
        var neededMinutes = DecayWall.MinutesPerDayToMakeReachable(plan, horizonDays: horizon);
        var reachable = DecayWall.ReachablePrefix(plan, objective.MinutesPerDay, horizon);

        objective.RecordCompile(_clock.UtcNow, sequence: 0);
        await _db.SaveChangesAsync(ct);

        return Result<CompiledPlan>.Success(new CompiledPlan(
            plan,
            verdict,
            Believable: false,
            Alternative: new NarrowerObjective(
                Steps: reachable,
                MinutesPerDayThatWouldWork: neededMinutes,
                ConceptsReachable: reachable.Count,
                ConceptsInObjective: plan.Steps.Count)));
    }

    /// A learner with a deadline is asking a sharper question, and gets a sharper answer.
    private int HorizonFor(Objective objective)
    {
        if (objective.Deadline is not { } deadline)
        {
            return DecayWall.DefaultHorizonDays;
        }

        var days = (int)Math.Ceiling((deadline - _clock.UtcNow).TotalDays);

        return Math.Max(1, days);
    }
}

/// A plan, and PRAXIS's honest verdict on it.
public sealed record CompiledPlan(
    Plan Plan,
    WallVerdict Wall,

    /// **False means PRAXIS does not believe this learner will get there** at the pace they gave.
    /// The plan is still returned — they are entitled to see it — but the product refuses to pretend.
    bool Believable,

    /// What PRAXIS offers instead. Null when the objective is reachable and nothing needs saying.
    NarrowerObjective? Alternative);

/// > *"At 20 minutes a day you will not become an AI Engineer inside eighteen months. You would hold
/// > about half of it, and spend most of your time keeping what you have. What you CAN become is
/// > [this]. Shall I compile that instead — or can you find 45 minutes?"*
public sealed record NarrowerObjective(
    IReadOnlyList<PlanStep> Steps,

    /// The number, not the platitude. Null when even a very large day would not do it — at which
    /// point the objective is wrong, not the schedule, and PRAXIS should say *that*.
    int? MinutesPerDayThatWouldWork,

    int ConceptsReachable,
    int ConceptsInObjective);
