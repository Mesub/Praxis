using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Shared;
using KernelHintRung = Praxis.Modules.Kernel.Events.HintRung;

namespace Praxis.Modules.Tutor.Features.Sessions;

/// **The learner asks for help, and every gate in the product fires at once.**
///
/// This is the busiest slice in PRAXIS, and it is worth reading top to bottom, because it is where
/// four separate refusals meet:
///
/// 1. **The ladder** (M6.2) decides whether this rung may be served at all — no skipping, no two
///    tutor turns in a row, a surcharge for asking again, and descent always free.
/// 2. **The budget** (M6.1) decides whether they can afford it. Running out is not a route to the
///    answer; it is a route to a stuck note and a recompiled plan.
/// 3. **The AI plane** (M6.3) assembles a context that *cannot* contain the answer.
/// 4. **The event log** records what was spent, so the Twin can discount the evidence that follows
///    — *"you did not produce that alone"* (I-5).
///
/// Note the order. The budget is checked and the event is appended **before** the model is called,
/// so a learner cannot get a hint that was never paid for by killing the connection halfway.
public static class TakeHint
{
    /// `LearnerSaid` is what they typed in reply to the last turn. **LR-3**: the ladder does not
    /// advance without it — a hint delivered to somebody who has not spoken yet is a lecture with a
    /// price tag.
    ///
    /// Their words are **data**. They are appended as an utterance and rendered to the tutor as a
    /// quoted one; they are never spliced into its instructions.
    public sealed record Command(Guid SessionId, KernelHintRung Rung, string? LearnerSaid);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();

            RuleFor(c => c.Rung)
                .IsInEnum()
                .WithMessage(
                    "That is not a rung on the ladder. There are five, and the deepest one gives you "
                    + "the structure with the hole still in it — there is nothing below it.");
        }
    }

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IAiPlane _ai;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            TutorDbContext db,
            IEventStore events,
            ProjectionRunner projections,
            IAiPlane ai,
            IUserContext user,
            IClock clock)
            => (_db, _events, _projections, _ai, _user, _clock) = (db, events, projections, ai, user, clock);

        public async Task<Result<TurnResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var session = await _db.Sessions
                .FirstOrDefaultAsync(s => s.Id == command.SessionId && s.LearnerId == learner, ct);

            if (session is null)
            {
                return Result<TurnResponse>.NotFound("No such session.");
            }

            if (session.State is not SessionState.Open)
            {
                return Result<TurnResponse>.Conflict($"This session is already {session.State}.");
            }

            // ── The learner spoke, and only now may the ladder move (LR-3) ───
            if (!string.IsNullOrWhiteSpace(command.LearnerSaid))
            {
                await _events.AppendAsync(
                    learner,
                    new LearnerResponded(session.Id, command.LearnerSaid),
                    _clock.UtcNow,
                    session.ConceptId,
                    ct);

                await _projections.CatchUpAsync(ct);

                await _db.Entry(session).ReloadAsync(ct);
            }

            var budget = await _db.Budgets
                .FirstOrDefaultAsync(b => b.LearnerId == learner && b.ConceptId == session.ConceptId, ct)
                ?? AnswerBudget.For(learner, session.ConceptId);

            // ── The ladder ───────────────────────────────────────────────────
            var considered = HintLadder.Consider(command.Rung, session.Ladder, budget);

            if (!considered.IsSuccess)
            {
                if (!HintLadder.IsExhaustion(considered))
                {
                    // "Not yet." They have the money; they have not earned the rung.
                    return Result<TurnResponse>.Invalid(considered.Message!);
                }

                // ── The budget is gone. **This is not a route to the answer.** ─
                //
                // The item stops. The learner writes down where their reasoning broke, and that note
                // — misconception evidence, never mastery evidence — is what the Compiler recompiles
                // the plan around. Going broke does not get you the answer. It gets you a better plan.
                await _events.AppendAsync(
                    learner,
                    new AnswerBudgetExhausted(
                        session.Id, budget.DeepestRung ?? KernelHintRung.Orient, budget.HintsTaken),
                    _clock.UtcNow,
                    session.ConceptId,
                    ct);

                await _projections.CatchUpAsync(ct);

                return Result<TurnResponse>.Forbidden(considered.Message!);
            }

            var grant = considered.Value;

            // ── Pay first. ───────────────────────────────────────────────────
            //
            // The event goes on the log *before* the model is called, so a learner cannot take a hint
            // they never paid for by killing the connection halfway through it.
            var spent = await _events.AppendAsync(
                learner,
                new HintTaken(session.Id, grant.Rung, budget.Remaining - grant.Cost),
                _clock.UtcNow,
                session.ConceptId,
                ct);

            if (!spent.IsSuccess)
            {
                return Result<TurnResponse>.Invalid(spent.Message!);
            }

            await _projections.CatchUpAsync(ct);

            // ── Then ask the tutor, which does not have the answer ───────────
            var reply = await _ai.TutorTurnAsync(
                new TutorTurnRequest(
                    ConceptSummary: session.ConceptId.ToString(),
                    ItemStem: "the current problem",
                    Transcript: string.IsNullOrWhiteSpace(command.LearnerSaid)
                        ? []
                        : [new TurnSpec("learner", command.LearnerSaid)],
                    Rung: (Infrastructure.Ai.HintRung)grant.Rung,
                    BudgetRemaining: budget.Remaining - grant.Cost,
                    KnownMisconceptions: [],
                    LearnerId: learner.ToString(),
                    ConceptId: session.ConceptId.ToString()),
                ct);

            if (!reply.IsSuccess)
            {
                // The hint was paid for and the model could not be reached. PRAXIS says so, and does
                // not invent a hint to fill the silence.
                return reply.CarryFailure<TurnResponse>();
            }

            var fresh = await Reload(learner, session.Id, session.ConceptId, ct);

            return Result<TurnResponse>.Success(new TurnResponse(
                Tutor: reply.Value.Text,
                Rung: grant.Rung.ToString(),
                Cost: grant.Cost,
                Session: fresh));
        }

        private async Task<SessionResponse> Reload(Guid learner, Guid sessionId, Guid concept, CancellationToken ct)
        {
            var session = await _db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);

            var budget = await _db.Budgets.AsNoTracking()
                .FirstOrDefaultAsync(b => b.LearnerId == learner && b.ConceptId == concept, ct)
                ?? AnswerBudget.For(learner, concept);

            return session.ToResponse(budget);
        }
    }
}
