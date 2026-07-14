using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Features.States;

/// **The learner's whole mind, concept by concept** — every concept they have touched, with its
/// state as of right now.
///
/// The summary (`GetTwin`) gives aggregate counts and the rot forecast; that is enough to *describe*
/// a mind but not to *draw* one. The Graph map needs to know **which** concept is held, which is
/// slipping, which has lapsed — so this returns the per-concept belief the map shades each node with.
///
/// It reads the learner off the token — there is no `?learnerId=`, here as everywhere in the Twin
/// (CANON §5.9). A concept the learner has never touched is simply **absent**: there is no belief to
/// report, and the map renders it as *unseen*, which is the honest day-zero claim rather than a
/// fabricated "knows nothing". Decay is applied at read time, exactly as the summary applies it, so a
/// node's shade is as current as the moment it was drawn.
public static class GetTwinStates
{
    public record Query;

    public sealed class Handler
    {
        private readonly TwinDbContext _db;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(TwinDbContext db, IUserContext user, IClock clock)
            => (_db, _user, _clock) = (db, user, clock);

        public async Task<Result<IReadOnlyList<TwinConceptStateResponse>>> HandleAsync(
            Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();
            var now = _clock.UtcNow;

            var states = await _db.States.AsNoTracking()
                .Where(s => s.LearnerId == learner)
                .ToListAsync(ct);

            var live = states
                .Select(s => LiveTwin.Project(s, [], now))
                .Select(s => new TwinConceptStateResponse(
                    s.ConceptId, StateOf(s), s.PKnows, s.Retrievability))
                .ToList();

            return Result<IReadOnlyList<TwinConceptStateResponse>>.Success(live);
        }

        /// The discrete state the map shades by — decided here, once, against the Twin's own
        /// thresholds, so a client can never drift from them. `decaying` and `lapsed` are both *held*
        /// concepts on the way down; `held` alone means fresh; a row that is not held is still being
        /// worked (it only exists once there is evidence).
        private static string StateOf(ConceptStateResponse s)
            => LiveTwin.HasLapsed(s) ? "lapsed"
                : LiveTwin.IsDecaying(s) ? "decaying"
                : s.IsHeld ? "held"
                : "inProgress";
    }
}

/// One concept's place in the learner's mind, for the map: its state, and the two numbers behind it.
public sealed record TwinConceptStateResponse(
    Guid ConceptId,
    string State,
    double PKnows,
    double Retrievability);
