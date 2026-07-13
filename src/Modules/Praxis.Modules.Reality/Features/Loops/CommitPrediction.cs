using FluentValidation;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Features.Loops;

/// **You say what the code will do, and how sure you are — and then it is on the board and you cannot
/// take it back.**
///
/// The whole epistemic value of a Reality Loop is the *order of events*: the claim and the confidence
/// are frozen **before** the code runs (C-6). So this endpoint appends a `PredictionMade` and stops —
/// it does not run anything, and it cannot, because the resolution is a separate act (M11.10c) that
/// *references* this commitment. A caller holding no commit has nothing to resolve.
///
/// The code is not stored. It is not a secret — the learner read it — and PRAXIS keeps only the
/// falsifiable commitment: the claim, and the number that makes it scoreable. The program itself is
/// re-supplied at resolution time, because the one thing PRAXIS must never hold is what the code
/// *does*: that does not exist yet, and by the time it does the learner has already committed.
public static class CommitPrediction
{
    public sealed record Command(PredictionKind Kind, string Code, string Answer, double Confidence);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Kind).IsInEnum();

            RuleFor(c => c.Code)
                .NotEmpty()
                .WithMessage("There is no code to predict about. Send the program the learner read.")
                .MaximumLength(20_000);

            RuleFor(c => c.Answer)
                .NotEmpty()
                .WithMessage("A prediction with no claim is not a prediction.")
                .MaximumLength(8_000);

            RuleFor(c => c.Confidence)
                .InclusiveBetween(0.0, 1.0)
                .WithMessage(
                    "Confidence is 0 to 1, and it is not optional: a prediction PRAXIS cannot score "
                    + "against how sure you were teaches you nothing about your calibration, which is "
                    + "the only reason this Loop exists.");
        }
    }

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(IEventStore events, IUserContext user, IClock clock)
            => (_events, _user, _clock) = (events, user, clock);

        public async Task<Result<CommitResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            // The domain owns the rule that a forecast without a probability is unscoreable — the
            // validator mirrors it for a clean 400, and this is the belt-and-braces so the invariant
            // holds even if the two ever drift.
            var committed = BehaviourPrediction.Commit(
                new PredictionQuestion(command.Kind, command.Code),
                command.Answer,
                command.Confidence);

            if (!committed.IsSuccess)
            {
                return committed.CarryFailure<CommitResponse>();
            }

            var loopId = Guid.CreateVersion7();

            // A code Loop resolves on demand, so its horizon is now: the board shows it as resolving
            // today, and the learner settles it whenever they choose to run the code.
            var appended = await _events.AppendAsync(
                learner,
                new PredictionMade(
                    loopId,
                    committed.Value.Answer,
                    committed.Value.Confidence,
                    _clock.UtcNow,
                    // The subject is frozen with the claim, so resolution runs *this* code, not a
                    // substitute a learner wrote to make their own prediction come true (which would
                    // manufacture mastery — a resolution moves pKnows).
                    Kind: committed.Value.Kind.ToString(),
                    Subject: command.Code),
                _clock.UtcNow,
                conceptId: null,
                ct);

            if (!appended.IsSuccess)
            {
                return appended.CarryFailure<CommitResponse>();
            }

            return Result<CommitResponse>.Success(new CommitResponse(
                loopId, committed.Value.Kind.ToString(), committed.Value.Answer, committed.Value.Confidence));
        }
    }
}

/// The receipt for a commitment. `LoopId` is how the learner points reality at it later — it is the
/// only handle, and there is no endpoint that edits or withdraws what it names.
public sealed record CommitResponse(Guid LoopId, string Kind, string Answer, double Confidence);
