using FluentValidation;
using Praxis.Modules.Graph.Queries;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

/// What the learner is ready for, and how far away everything else is.
///
/// **The Graph does not know what a learner knows, and must never guess.** Held concepts are
/// supplied by the Twin (M3), which derives them from evidence. If this slice ever starts
/// inferring what someone holds, two engines will disagree about the same person and both will
/// be wrong.
public static class GetFrontier
{
    public record Query(string Domain, IReadOnlyList<Guid> Held);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(x => x.Domain).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Held).NotNull();
        }
    }

    public sealed class Handler
    {
        private readonly GraphQueries _queries;

        public Handler(GraphQueries queries) => _queries = queries;

        public async Task<Result<FrontierResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var domain = query.Domain.Trim().ToLowerInvariant();
            var held = query.Held ?? [];

            var ready = await _queries.FrontierAsync(domain, [.. held], ct);
            var blocked = await _queries.BlockedAsync(domain, [.. held], ct);

            return Result<FrontierResponse>.Success(new FrontierResponse(
                [.. ready.Select(c => c.ToResponse())],
                [.. blocked.Select(b => new BlockedResponse(b.ConceptId, b.Slug, b.MissingPrerequisites))]));
        }
    }
}

public sealed record FrontierResponse(
    /// Start any of these today. Nothing stands in the way.
    IReadOnlyList<ConceptResponse> Ready,

    /// Not yet — and how far. "Two concepts away" is a ladder; "not ready" is a wall.
    IReadOnlyList<BlockedResponse> Blocked);

public sealed record BlockedResponse(Guid ConceptId, string Slug, int MissingPrerequisites);
