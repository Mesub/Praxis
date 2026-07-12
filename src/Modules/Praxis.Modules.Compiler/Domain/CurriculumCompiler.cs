using Praxis.Shared;

namespace Praxis.Modules.Compiler.Domain;

/// **`make` for the mind.**
///
/// A roadmap is not authored. It is **compiled**: target + dependency DAG + what the learner
/// already holds (the cache) + how fast it is rotting (cache invalidation) → the shortest honest
/// path to competence.
///
/// ## This is a solver, and that is a decision
///
/// There is **no language model anywhere in this path**, and there never will be (CANON I-4, doc 24
/// §3.4). A plan produced by prompting a model is a listicle: it does not know what this person
/// holds, it cannot price the work, it cannot see the wall coming, and it will produce a different
/// answer if you ask it twice. It also costs money every night, for every learner, to produce
/// something a graph algorithm produces exactly and for free.
///
/// The plan is therefore a pure function of (objective, graph, twin, constraints). Compile the same
/// learner twice and you get the same plan — which is the only reason a learner could ever trust it.
///
/// ## Why it is not textbook Dijkstra
///
/// Dijkstra finds the cheapest route to a node when you may choose *between* paths. Here you may
/// not: **every prerequisite is required.** You cannot reach `retrieval` by the cheap parent and
/// skip the expensive one — you need both. So the "path" is the whole transitive closure, and the
/// algorithm's job is to *order* it.
///
/// What survives from Dijkstra is the greedy frontier: repeatedly take the cheapest concept whose
/// prerequisites are all satisfied. That ordering is what keeps foundations fresh when their
/// dependents arrive — which is exactly what the cost function (M4.1) is pricing.
public static class CurriculumCompiler
{
    /// Below this, a concept the learner "holds" has rotted far enough that they will not be able
    /// to build on it. It goes back in the plan as a refresh — because the alternative is walking
    /// them into a wall and letting them think it is their fault.
    public const double FreshEnough = 0.70;

    public static Result<Plan> Compile(CompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MinutesPerDay <= 0)
        {
            return Result<Plan>.Invalid("A plan needs some minutes per day to be compiled against.");
        }

        var byId = request.Concepts.ToDictionary(c => c.Id);

        if (!byId.TryGetValue(request.Objective, out _))
        {
            return Result<Plan>.NotFound("The objective is not in the graph it was compiled against.");
        }

        // ── What must be done ────────────────────────────────────────────────
        //
        // Everything the objective transitively requires, plus the objective itself. All of it —
        // there is no choosing a cheaper parent.
        var required = new HashSet<Guid>();

        if (!Collect(request.Objective, byId, required, []))
        {
            return Result<Plan>.Invalid(
                "The graph contains a REQUIRES cycle reachable from this objective, so there is no "
                + "order in which it could be learned. The pack validator refuses to publish one, "
                + "which means this graph was written outside the gate.");
        }

        // ── What is already in the cache ─────────────────────────────────────
        var satisfied = new HashSet<Guid>();
        var alreadyHeld = 0;

        foreach (var id in required)
        {
            var c = byId[id];

            if (c.IsHeld && c.Retrievability >= FreshEnough)
            {
                satisfied.Add(id);
                alreadyHeld++;
            }
        }

        var todo = required.Where(id => !satisfied.Contains(id)).ToHashSet();

        // ── Order it ─────────────────────────────────────────────────────────
        //
        // Greedy frontier: repeatedly take the cheapest concept whose prerequisites are all
        // satisfied. Cheapest-first keeps foundations fresh when their dependents arrive, and
        // gets the learner producing evidence sooner — which matters, because a learner who
        // produces nothing for three weeks stops being a learner.
        var steps = new List<PlanStep>();
        var cumulative = 0.0;
        var refreshes = 0;

        while (todo.Count > 0)
        {
            var available = todo
                .Where(id => byId[id].Requires.All(p => !required.Contains(p) || satisfied.Contains(p)))
                .ToList();

            if (available.Count == 0)
            {
                // Unreachable: every remaining concept is blocked by another remaining concept.
                // With a cycle already ruled out, this cannot happen — but if it ever does, say so
                // rather than silently dropping the tail of someone's curriculum.
                return Result<Plan>.Invalid(
                    $"{todo.Count} concept(s) are unreachable from the objective's prerequisites. "
                    + "The graph is not a DAG, and no plan over it can be trusted.");
            }

            var next = available
                .Select(id => (Id: id, Cost: CostOf(byId[id], byId, satisfied, required)))
                .OrderBy(x => x.Cost)
                .ThenBy(x => byId[x.Id].Slug, StringComparer.Ordinal)   // deterministic ties
                .First();

            var concept = byId[next.Id];
            cumulative += next.Cost;

            var kind = Kind(concept);

            if (kind == StepKind.Refresh)
            {
                refreshes++;
            }

            steps.Add(new PlanStep(
                ConceptId: concept.Id,
                Slug: concept.Slug,
                Position: steps.Count + 1,
                Minutes: next.Cost,
                CumulativeMinutes: cumulative,
                DayReached: (int)Math.Ceiling(cumulative / request.MinutesPerDay),
                Kind: kind));

            satisfied.Add(next.Id);
            todo.Remove(next.Id);
        }

        return Result<Plan>.Success(new Plan(
            Objective: request.Objective,
            Steps: steps,
            TotalMinutes: cumulative,
            ConceptsAlreadyHeld: alreadyHeld,
            ConceptsNeedingRefresh: refreshes,
            DaysAtStatedPace: cumulative / request.MinutesPerDay));
    }

    /// The cost of a concept **at the moment the plan reaches it**.
    ///
    /// Prerequisites the learner already holds are priced at their *current* retrievability — they
    /// may be rotting, and that makes this concept harder. Prerequisites scheduled earlier in this
    /// plan are treated as fresh, because the learner will have just done them.
    ///
    /// That asymmetry is the whole point of ordering: the plan actively keeps foundations warm.
    private static double CostOf(
        CompilerConcept concept,
        Dictionary<Guid, CompilerConcept> byId,
        HashSet<Guid> satisfied,
        HashSet<Guid> required)
    {
        var prerequisiteRetrievability = concept.Requires
            .Where(byId.ContainsKey)
            .Select(p =>
            {
                // Scheduled in this plan and already passed: they have just done it.
                if (required.Contains(p) && satisfied.Contains(p) && !byId[p].IsHeld)
                {
                    return 1.0;
                }

                // Held from before: however fresh — or stale — the Twin says it is.
                return byId[p].Retrievability;
            })
            .ToList();

        return ConceptCost.Minutes(new ConceptCostInputs(
            concept.Id,
            concept.EstimatedMinutes,
            concept.MasteryCredit,
            concept.LearnerDifficulty,
            concept.Contract,
            prerequisiteRetrievability));
    }

    private static StepKind Kind(CompilerConcept c)
    {
        // Held once, and let rot. They will meet it again — cheaper than the first time, because
        // relearning is faster than learning, but NOT free. The plan refuses to pretend it is done.
        if (c.IsHeld)
        {
            return StepKind.Refresh;
        }

        return c.MasteryCredit > 0 ? StepKind.Finish : StepKind.Learn;
    }

    /// Depth-first closure with cycle detection. An explicit stack, because a deep graph should not
    /// be able to blow the real one.
    private static bool Collect(
        Guid id,
        Dictionary<Guid, CompilerConcept> byId,
        HashSet<Guid> collected,
        HashSet<Guid> visiting)
    {
        if (collected.Contains(id))
        {
            return true;
        }

        if (!visiting.Add(id))
        {
            return false;   // cycle
        }

        if (byId.TryGetValue(id, out var concept))
        {
            foreach (var prerequisite in concept.Requires)
            {
                if (!Collect(prerequisite, byId, collected, visiting))
                {
                    return false;
                }
            }
        }

        visiting.Remove(id);
        collected.Add(id);

        return true;
    }
}
