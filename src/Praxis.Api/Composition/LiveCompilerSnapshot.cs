using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Compiler;
using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Graph.Queries;
using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **Where the Graph and the Twin are joined, and the only place it happens.**
///
/// The Graph knows the shape of the subject. The Twin knows the shape of the learner. Neither
/// references the other, and the Compiler references neither — it is handed one flat list of values
/// and returns a plan.
///
/// The decay is applied *here*, at read time, exactly as the Twin's own API does (M3.7b): the stored
/// state records what was proved and when; what the learner can retrieve *today* is computed on the
/// way out. A plan compiled against yesterday's retrievability is a plan for yesterday's person.
public sealed class LiveCompilerSnapshot : ICompilerSnapshot
{
    private readonly GraphDbContext _graph;
    private readonly TwinDbContext _twin;
    private readonly GraphQueries _queries;
    private readonly IClock _clock;

    public LiveCompilerSnapshot(
        GraphDbContext graph,
        TwinDbContext twin,
        GraphQueries queries,
        IClock clock)
        => (_graph, _twin, _queries, _clock) = (graph, twin, queries, clock);

    public async Task<IReadOnlyList<CompilerConcept>> ForObjectiveAsync(
        Guid learnerId,
        Guid objectiveConceptId,
        CancellationToken ct = default)
    {
        var objective = await _graph.Concepts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == objectiveConceptId, ct);

        if (objective is null)
        {
            return [];
        }

        // Everything the objective transitively needs, plus the objective itself.
        var prerequisites = await _queries.PrerequisitesOfAsync(objectiveConceptId, ct);

        var ids = prerequisites.Select(c => c.Id).Append(objectiveConceptId).ToHashSet();

        var concepts = await _graph.Concepts
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        var contracts = await _graph.Contracts
            .AsNoTracking()
            .Where(c => ids.Contains(c.ConceptId))
            .ToDictionaryAsync(c => c.ConceptId, ct);

        var edges = await _graph.Edges
            .AsNoTracking()
            .Where(e => e.Kind == EdgeKind.Requires && ids.Contains(e.ToConceptId))
            .ToListAsync(ct);

        var requires = edges
            .GroupBy(e => e.ToConceptId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(e => e.FromConceptId)]);

        var states = await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId && ids.Contains(s.ConceptId))
            .ToDictionaryAsync(s => s.ConceptId, ct);

        var now = _clock.UtcNow;

        return
        [
            .. concepts
                .Where(c => contracts.ContainsKey(c.Id))
                .Select(c =>
                {
                    var state = states.GetValueOrDefault(c.Id);
                    var contract = contracts[c.Id];

                    return new CompilerConcept(
                        Id: c.Id,
                        Slug: c.Slug,
                        Requires: requires.GetValueOrDefault(c.Id, []),
                        EstimatedMinutes: c.EstimatedMinutes,
                        Contract: ToContract(contract),
                        MasteryCredit: state?.MasteryCredit ?? 0.0,
                        IsHeld: state?.IsHeld ?? false,
                        Retrievability: RetrievabilityNow(state, now),
                        LearnerDifficulty: state?.Difficulty ?? ConceptState.DefaultDifficulty);
                }),
        ];
    }

    /// How much of it they could actually retrieve **today** — not on the day they proved it.
    private static double RetrievabilityNow(ConceptState? state, DateTime now)
    {
        if (state is null || state.Stability <= 0 || state.LastEvidenceAt is null)
        {
            return 0.0;
        }

        var elapsed = Math.Max(0.0, (now - state.LastEvidenceAt.Value).TotalDays);

        return Fsrs.Retrievability(state.Stability, elapsed);
    }

    private static MasteryContract ToContract(PoMContract contract)
        => new(
            contract.HoldThreshold,
            [.. contract.Accepted.Select(a => new EvidenceClause(
                a.Type, a.MinScore, a.MaxContribution, a.RequiredCount))]);
}
