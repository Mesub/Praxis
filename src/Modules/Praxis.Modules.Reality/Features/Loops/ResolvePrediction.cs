using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Features.Loops;

/// **Reality settles it — and only now is it safe to know what the code does.**
///
/// The learner committed; the program was frozen with the claim (M11.10c-1). So resolution reads the
/// *stored* subject, runs it in the sandbox, and compares reality to what they said. It cannot be
/// gamed by supplying different code, and it cannot be run twice: a resolved loop is closed forever,
/// because the calibration record it feeds is only honest if each commitment is judged exactly once.
///
/// **A sandbox outage is a failure, never a verdict.** If Docker is down the resolver carries the
/// failure back intact — the prediction stays open, nothing is written, and the learner is not marked
/// wrong because our daemon was restarting (M9.3). A wrong resolution would poison the one record the
/// whole Loop exists to keep honest.
public static class ResolvePrediction
{
    public sealed record Command(Guid LoopId);

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly ISandbox _sandbox;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(IEventStore events, ISandbox sandbox, IUserContext user, IClock clock)
            => (_events, _sandbox, _user, _clock) = (events, sandbox, user, clock);

        public async Task<Result<ResolveResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var stream = await _events.ReadStreamAsync(learner, ct: ct);

            PredictionMade? made = null;

            foreach (var e in stream)
            {
                switch (e.Type)
                {
                    case LearningEventType.PredictionMade:
                        var candidate = EventCatalogue.Deserialize<PredictionMade>(e);
                        if (candidate.LoopId == command.LoopId)
                        {
                            made = candidate;
                        }

                        break;

                    case LearningEventType.PredictionResolved:
                        var resolved = EventCatalogue.Deserialize<PredictionResolved>(e);
                        if (resolved.LoopId == command.LoopId)
                        {
                            // Closed forever. Resolving again would judge one commitment twice.
                            return Result<ResolveResponse>.Conflict(
                                "This prediction has already resolved. A commitment is judged once — "
                                + "resolving it again would let a learner keep rolling the dice until "
                                + "reality was kind.");
                        }

                        break;
                }
            }

            if (made is null)
            {
                return Result<ResolveResponse>.NotFound("No open prediction with that id.");
            }

            if (!Enum.TryParse<PredictionKind>(made.Kind, out var kind) || made.Subject.Length == 0)
            {
                return Result<ResolveResponse>.Invalid(
                    "This loop has no runnable subject, so reality cannot settle it here.");
            }

            var resolution = await new BehaviourResolver(_sandbox).ResolveAsync(
                new PredictionQuestion(kind, made.Subject),
                new CommittedPrediction(kind, made.Claim, made.StatedConfidence),
                ct);

            if (!resolution.IsSuccess)
            {
                // The sandbox failed, not the learner. The loop stays open; nothing is written.
                return resolution.CarryFailure<ResolveResponse>();
            }

            var verdict = resolution.Value;

            var appended = await _events.AppendAsync(
                learner,
                new PredictionResolved(command.LoopId, verdict.Correct, verdict.BrierScore, verdict.GroundTruth),
                _clock.UtcNow,
                conceptId: null,
                ct);

            if (!appended.IsSuccess)
            {
                return appended.CarryFailure<ResolveResponse>();
            }

            return Result<ResolveResponse>.Success(new ResolveResponse(
                command.LoopId,
                verdict.Correct,
                verdict.BrierScore,
                verdict.Predicted,
                verdict.GroundTruth,
                verdict.Confidence));
        }
    }
}

/// **What they said, what reality did, and the Brier score that lands in calibration.** The Brier is
/// about how sure they were, not whether they were right — sure-and-wrong is the most expensive score
/// there is, and it is only sayable because the confidence was frozen before the code ran (C-6).
public sealed record ResolveResponse(
    Guid LoopId,
    bool Correct,
    double BrierScore,
    string Predicted,
    string GroundTruth,
    double Confidence);
