using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment;
using Praxis.Modules.Graph.Persistence;

namespace Praxis.Api.Composition;

/// **The seam between the Graph and Assessment's item generator.**
///
/// The Graph owns a concept's summary, difficulty prior and declared misconception library;
/// Assessment needs all three to generate items, but must not know the Graph exists — exactly the
/// shape of the tutor's `IMisconceptionLibrary` (M13.2b-ii). Neither module references the other —
/// this adapter, at the composition root, carries the data across. If a future engine needs the
/// same, it implements its own port; it does not reach into the Graph's tables.
public sealed class GraphConceptIntel : IConceptIntel
{
    private readonly GraphDbContext _db;

    public GraphConceptIntel(GraphDbContext db) => _db = db;

    public async Task<ConceptIntel?> ForAsync(Guid conceptId, CancellationToken ct = default)
    {
        var concept = await _db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conceptId, ct);

        if (concept is null)
        {
            return null;
        }

        var declared = await _db.Misconceptions.AsNoTracking()
            .Where(m => m.ConceptId == conceptId)
            .OrderBy(m => m.Key)
            .ToDictionaryAsync(m => m.Key, m => m.Statement, ct);

        return new ConceptIntel(
            concept.Summary,
            Math.Clamp((concept.DifficultyPrior + 3.0) / 6.0, 0.0, 1.0),
            declared);
    }
}
