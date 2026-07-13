using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Features.Loops;

/// **The board is a projection of the log, computed on read — never stored.**
///
/// A learner's open loops are exactly the `PredictionMade` events with no matching
/// `PredictionResolved` yet; a resolved loop is the pair. So the board is *derived*, the same way the
/// compiler's plan is derived: recomputed on every request from the events that cannot be taken back,
/// because a stored board would be a second copy of the truth and the copy is the one somebody edits
/// to make an uncomfortable prediction quietly disappear. Here there is nothing to edit — the events
/// are immutable, and the board is a pure function of them and the clock.
public static class GetLoopsBoard
{
    public sealed record Query;

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(IEventStore events, IUserContext user, IClock clock)
            => (_events, _user, _clock) = (events, user, clock);

        public async Task<Result<LoopsBoardView>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var stream = await _events.ReadStreamAsync(learner, ct: ct);

            return Result<LoopsBoardView>.Success(LoopsBoard.Build(Fold(stream), _clock.UtcNow));
        }

        /// Fold the learner's prediction events into loops. `PredictionMade` opens one; the matching
        /// `PredictionResolved` closes it, carrying the outcome and the Brier score. A resolution with
        /// no matching commitment is ignored rather than invented — there is no loop to close.
        internal static IReadOnlyList<Loop> Fold(IReadOnlyList<LearningEvent> stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            var loops = new Dictionary<Guid, Loop>();

            foreach (var e in stream)
            {
                switch (e.Type)
                {
                    case LearningEventType.PredictionMade:
                        var made = EventCatalogue.Deserialize<PredictionMade>(e);
                        loops[made.LoopId] = new Loop(
                            made.LoopId,
                            made.Claim,
                            made.StatedConfidence,
                            e.OccurredAt,
                            made.ResolvesAt,
                            LoopStatus.Open);
                        break;

                    case LearningEventType.PredictionResolved:
                        var resolved = EventCatalogue.Deserialize<PredictionResolved>(e);
                        if (loops.TryGetValue(resolved.LoopId, out var open))
                        {
                            loops[resolved.LoopId] = open with
                            {
                                Status = LoopStatus.Resolved,
                                Correct = resolved.Correct,
                                BrierScore = resolved.BrierScore,
                            };
                        }

                        break;
                }
            }

            return [.. loops.Values];
        }
    }
}
