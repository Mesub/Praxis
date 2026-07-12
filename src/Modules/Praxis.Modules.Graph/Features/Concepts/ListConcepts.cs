using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

public static class ListConcepts
{
    public record Query(string Domain, int Page = 1, int PageSize = 50, string? Search = null);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(x => x.Domain).NotEmpty().MaximumLength(60);
            RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        }
    }

    public sealed class Handler
    {
        private readonly GraphDbContext _db;

        public Handler(GraphDbContext db) => _db = db;

        public async Task<Result<PagedResult<ConceptResponse>>> HandleAsync(
            Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var page = Math.Max(1, query.Page);
            var size = Math.Clamp(query.PageSize, 1, 200);
            var domain = query.Domain.Trim().ToLowerInvariant();

            var concepts = _db.Concepts.AsNoTracking().Where(c => c.Domain == domain);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();

                concepts = concepts.Where(c =>
                    EF.Functions.ILike(c.Slug, $"%{term}%")
                    || EF.Functions.ILike(c.Title, $"%{term}%"));
            }

            var total = await concepts.CountAsync(ct);

            var items = await concepts
                .OrderBy(c => c.DifficultyPrior)
                .ThenBy(c => c.Slug)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(ConceptResponse.Projection)
                .ToListAsync(ct);

            return Result<PagedResult<ConceptResponse>>.Success(
                new PagedResult<ConceptResponse>(items, total, page, size));
        }
    }
}
