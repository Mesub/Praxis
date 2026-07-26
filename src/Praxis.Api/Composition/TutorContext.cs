using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Tutor;

namespace Praxis.Api.Composition;

/// **The seam between the Assessment's item bank, the Graph's concepts, and the tutor.**
///
/// The tutor is given a passage and a stem it can point at, and a concept summary it can speak
/// from — never the options, because `TutorItemContext` has no field for them. Holding both
/// `AssessmentDbContext` and `GraphDbContext` in one class would be a violation anywhere else in
/// this codebase ("no module reads another module's tables"); it is legitimate exactly here,
/// because a composition-root adapter is what carries data across a seam neither module may cross
/// on its own. Neither module references the other.
public sealed class TutorContext : ITutorContext
{
    private readonly AssessmentDbContext _items;
    private readonly GraphDbContext _graph;

    public TutorContext(AssessmentDbContext items, GraphDbContext graph)
        => (_items, _graph) = (items, graph);

    public async Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default)
        => await _items.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => new TutorItemContext(i.Stem, i.Passage))
            .FirstOrDefaultAsync(ct);

    public async Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default)
        => await _graph.Concepts.AsNoTracking()
            .Where(c => c.Id == conceptId)
            .Select(c => c.Summary)
            .FirstOrDefaultAsync(ct);
}
