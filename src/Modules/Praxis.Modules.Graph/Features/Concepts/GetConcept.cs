using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

public static class GetConcept
{
    public record Query(string Slug);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() => RuleFor(x => x.Slug).NotEmpty().MaximumLength(120);
    }

    public sealed class Handler
    {
        private readonly GraphDbContext _db;

        public Handler(GraphDbContext db) => _db = db;

        public async Task<Result<ConceptDetailResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var slug = query.Slug.Trim().ToLowerInvariant();

            var concept = await _db.Concepts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == slug, ct);

            if (concept is null)
            {
                return Result<ConceptDetailResponse>.NotFound($"No concept called '{slug}'.");
            }

            var contract = await _db.Contracts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConceptId == concept.Id, ct);

            if (contract is null)
            {
                // The pack validator refuses to load a concept without a contract, so this means
                // the graph was written to by something that bypassed the gate.
                return Result<ConceptDetailResponse>.Invalid(
                    $"'{slug}' has no proof contract. Nothing could demonstrate it, so a learner "
                    + "could work on it forever without ever holding it. The graph was loaded "
                    + "outside the pack loader.");
            }

            var edges = await _db.Edges
                .AsNoTracking()
                .Where(e => e.ToConceptId == concept.Id || e.FromConceptId == concept.Id)
                .ToListAsync(ct);

            var relatedIds = edges
                .SelectMany(e => new[] { e.FromConceptId, e.ToConceptId })
                .Distinct()
                .ToList();

            var slugs = await _db.Concepts
                .AsNoTracking()
                .Where(c => relatedIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Slug, ct);

            var requires = edges
                .Where(e => e.Kind == EdgeKind.Requires && e.ToConceptId == concept.Id)
                .Select(e => slugs[e.FromConceptId])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            // Contrasts are undirected in meaning: whichever end the edge was authored from, the
            // learner confuses these two with each other.
            var contrasts = edges
                .Where(e => e.Kind == EdgeKind.Contrasts)
                .Select(e => e.FromConceptId == concept.Id ? e.ToConceptId : e.FromConceptId)
                .Distinct()
                .Select(id => slugs[id])
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            return Result<ConceptDetailResponse>.Success(new ConceptDetailResponse(
                concept.ToResponse(), requires, contrasts, contract.ToResponse()));
        }
    }
}
