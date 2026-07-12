using Praxis.Modules.Compiler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Features.Plans;

/// **The plan, and PRAXIS's honest verdict on it.**
///
/// This endpoint is the one place in the product where it is allowed to say *no* — and it is the
/// reason the whole Compiler exists. Every other learning product answers "can I do this?" with an
/// enthusiastic yes, because the yes is what gets paid for.
///
/// A plan is never stored. It is a pure function of (objective, graph, twin, minutes) and it is
/// recomputed on every request, because a cached plan is a plan for a learner who no longer exists.
public static class GetPlan
{
    public sealed record Query;

    public sealed class Handler
    {
        private readonly PlanService _plans;
        private readonly IUserContext _user;

        public Handler(PlanService plans, IUserContext user) => (_plans, _user) = (plans, user);

        public async Task<Result<PlanResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var result = await _plans.ForLearnerAsync(_user.RequireLearnerId(), ct);

            // The *kind* of refusal is carried, not flattened. "You have no objective" is a 404;
            // "your deadline is in the past" is a 400. Collapsing them would tell a learner with no
            // objective that their request was malformed.
            return result.IsSuccess
                ? Result<PlanResponse>.Success(PlanResponse.From(result.Value))
                : result.CarryFailure<PlanResponse>();
        }
    }
}

public sealed record PlanResponse(
    Guid Objective,
    IReadOnlyList<PlanStepResponse> Steps,
    double TotalMinutes,
    int ConceptsAlreadyHeld,
    int ConceptsNeedingRefresh,
    double DaysAtStatedPace,

    /// **False means PRAXIS does not believe this learner gets there at the pace they gave.**
    ///
    /// The plan is still returned — it is theirs, and they are entitled to see it. What is withheld
    /// is the pretence.
    bool Believable,

    /// The forward simulation: how much of the objective survives being maintained.
    WallResponse Wall,

    /// What PRAXIS offers instead, when it does not believe. Null when it does.
    AlternativeResponse? Alternative)
{
    public static PlanResponse From(CompiledPlan compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);

        return new PlanResponse(
            compiled.Plan.Objective,
            [.. compiled.Plan.Steps.Select(PlanStepResponse.From)],
            compiled.Plan.TotalMinutes,
            compiled.Plan.ConceptsAlreadyHeld,
            compiled.Plan.ConceptsNeedingRefresh,
            compiled.Plan.DaysAtStatedPace,
            compiled.Believable,
            new WallResponse(
                compiled.Wall.Reachable,
                compiled.Wall.ConceptsReached,
                compiled.Wall.ConceptsInPlan,
                compiled.Wall.DaysToObjective,
                compiled.Wall.HorizonDays,
                compiled.Wall.PeakMaintenanceMinutesPerDay,
                compiled.Wall.MaintenanceShareOfDay),
            compiled.Alternative is null
                ? null
                : new AlternativeResponse(
                    [.. compiled.Alternative.Steps.Select(PlanStepResponse.From)],
                    compiled.Alternative.MinutesPerDayThatWouldWork,
                    compiled.Alternative.ConceptsReachable,
                    compiled.Alternative.ConceptsInObjective));
    }
}

public sealed record PlanStepResponse(
    Guid ConceptId,
    string Slug,
    int Position,
    double Minutes,
    double CumulativeMinutes,
    int DayReached,
    string Kind)
{
    public static PlanStepResponse From(PlanStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new PlanStepResponse(
            step.ConceptId, step.Slug, step.Position,
            step.Minutes, step.CumulativeMinutes, step.DayReached,
            step.Kind.ToString());
    }
}

/// The forward simulation, shown to the learner rather than hidden behind the verdict.
///
/// **The maintenance share is the number nobody else in this industry will print**: at the worst
/// point, this much of their daily minutes goes on *keeping what they already have* rather than on
/// getting anywhere. When it approaches 1.0 they are running to stand still — and until PRAXIS
/// simulated it, they would have had no way to know that, and would have blamed themselves.
public sealed record WallResponse(
    bool Reachable,
    int ConceptsReached,
    int ConceptsInPlan,
    int DaysToObjective,
    int HorizonDays,
    double PeakMaintenanceMinutesPerDay,
    double MaintenanceShareOfDay);

/// > *"At 20 minutes a day you will not become an AI Engineer inside eighteen months. What you CAN
/// > become is this. Shall I compile that instead — or can you find 45 minutes?"*
public sealed record AlternativeResponse(
    IReadOnlyList<PlanStepResponse> Steps,

    /// The number, not the platitude. Null when even a very large day would not do it — at which
    /// point the objective is wrong, not the schedule.
    int? MinutesPerDayThatWouldWork,

    int ConceptsReachable,
    int ConceptsInObjective);
