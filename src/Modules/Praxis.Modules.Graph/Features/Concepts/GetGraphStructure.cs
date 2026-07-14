using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

/// **The whole domain graph in one read** — every concept and every prerequisite edge — so a client
/// can lay out the map without one round trip per concept's edges (`GetConcept` gives edges only for
/// a single node, which is fine for a detail panel and useless for drawing the whole DAG).
///
/// It is **pure structure**: no learner state crosses this boundary, because the Graph does not know
/// what anyone knows. The map's shading — held, decaying, lapsed — is the Twin's belief, fetched
/// separately and joined on the client. Keeping the two apart here is the same seam the whole
/// architecture keeps: the Graph says what the terrain *is*, the Twin says where the learner *stands*.
public static class GetGraphStructure
{
    public record Query(string Domain);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() => RuleFor(x => x.Domain).NotEmpty().MaximumLength(60);
    }

    public sealed class Handler
    {
        private readonly GraphDbContext _db;

        public Handler(GraphDbContext db) => _db = db;

        public async Task<Result<GraphStructureResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var domain = query.Domain.Trim().ToLowerInvariant();

            var nodes = await _db.Concepts.AsNoTracking()
                .Where(c => c.Domain == domain)
                .OrderBy(c => c.DifficultyPrior)
                .ThenBy(c => c.Slug)
                .Select(c => new GraphNode(c.Id, c.Slug, c.Title, c.DifficultyPrior))
                .ToListAsync(ct);

            if (nodes.Count == 0)
            {
                return Result<GraphStructureResponse>.NotFound(
                    $"There is no graph published for '{domain}'.");
            }

            var ids = nodes.Select(n => n.ConceptId).ToList();

            // Only REQUIRES — the ordering DAG the map draws. RELATES and CONTRASTS are real edges but
            // not *dependency* structure; drawing them here would turn a ladder into a hairball. Both
            // endpoints must be in this domain (a pack is one domain, but the guard is cheap and honest).
            var edges = await _db.Edges.AsNoTracking()
                .Where(e => e.Kind == EdgeKind.Requires
                    && ids.Contains(e.FromConceptId)
                    && ids.Contains(e.ToConceptId))
                .Select(e => new GraphEdge(e.FromConceptId, e.ToConceptId))
                .ToListAsync(ct);

            return Result<GraphStructureResponse>.Success(new GraphStructureResponse(nodes, edges));
        }
    }
}

/// The terrain: every node, every prerequisite arrow. What the learner's own position is laid over.
public sealed record GraphStructureResponse(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges);

public sealed record GraphNode(Guid ConceptId, string Slug, string Title, double Difficulty);

/// A prerequisite edge: `From` must be held before `To`. The direction the Compiler walks, and the
/// arrow the map draws — earlier things point at the later things they unlock.
public sealed record GraphEdge(Guid From, Guid To);
