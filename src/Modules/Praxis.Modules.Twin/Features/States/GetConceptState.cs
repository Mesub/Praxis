using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Features.States;

public static class GetConceptState
{
    public record Query(Guid ConceptId);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() => RuleFor(x => x.ConceptId).NotEmpty();
    }

    public sealed class Handler
    {
        private readonly TwinDbContext _db;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(TwinDbContext db, IUserContext user, IClock clock)
            => (_db, _user, _clock) = (db, user, clock);

        public async Task<Result<ConceptStateResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var state = await _db.States
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.LearnerId == learner && s.ConceptId == query.ConceptId, ct);

            if (state is null)
            {
                // Not "you know nothing" — *unknown*, which is a different claim and the honest one.
                // Return the day-zero belief rather than a 404: the learner exists, the concept
                // exists, and PRAXIS simply has no evidence about the two of them together.
                state = ConceptState.Unseen(learner, query.ConceptId);
            }

            var misconceptions = await _db.Misconceptions
                .AsNoTracking()
                .Where(m => m.LearnerId == learner && m.ConceptId == query.ConceptId)
                .ToListAsync(ct);

            return Result<ConceptStateResponse>.Success(
                LiveTwin.Project(state, misconceptions, _clock.UtcNow));
        }
    }
}
