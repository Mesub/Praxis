using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Tutor;

namespace Praxis.Api.Composition;

/// **The seam between the Graph's misconception library and the tutor.**
///
/// The Graph declares, validates and stores the named wrong beliefs (M13.2a/b-i); the tutor needs
/// only their keys, to hand to the AI plane as the closed set it may match a wrong answer against.
/// Neither module references the other — this adapter, at the composition root, carries the keys
/// across as plain strings. If a future engine needs the same, it implements its own port; it does
/// not reach into the Graph's tables.
public sealed class GraphMisconceptionLibrary : IMisconceptionLibrary
{
    private readonly GraphDbContext _db;

    public GraphMisconceptionLibrary(GraphDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> KeysForConceptAsync(
        Guid conceptId, CancellationToken ct = default)
        => await _db.Misconceptions
            .AsNoTracking()
            .Where(m => m.ConceptId == conceptId)
            .OrderBy(m => m.Key)
            .Select(m => m.Key)
            .ToListAsync(ct);
}
