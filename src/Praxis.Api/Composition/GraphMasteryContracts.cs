using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Twin;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **The seam between the Graph and the Twin, and the only place the two meet.**
///
/// The Graph authors contracts and refuses to write a dishonest one. The Twin applies them and
/// refuses to be fooled by one. Neither project references the other; the contract crosses as a
/// value, and this adapter — living at the composition root, where the application is assembled —
/// is what carries it.
///
/// If a future engine needs the same thing, it implements its own port. It does not reach into the
/// Graph's tables.
public sealed class GraphMasteryContracts : IMasteryContracts
{
    private readonly GraphDbContext _db;

    public GraphMasteryContracts(GraphDbContext db) => _db = db;

    public async Task<MasteryContract?> ForConceptAsync(Guid conceptId, CancellationToken ct = default)
    {
        var contract = await _db.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConceptId == conceptId, ct);

        if (contract is null)
        {
            return null;
        }

        return new MasteryContract(
            contract.HoldThreshold,
            [.. contract.Accepted.Select(a => new EvidenceClause(
                a.Type, a.MinScore, a.MaxContribution, a.RequiredCount))]);
    }
}
