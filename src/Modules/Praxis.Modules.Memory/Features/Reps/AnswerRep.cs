using FluentValidation;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Memory.Features.Reps;

/// **A rep is not special.**
///
/// This is the whole of M8.3 and it is worth being precise about, because "not special" is doing a
/// lot of work.
///
/// ## Three ways a rep could have been special, and is not
///
/// **1. It cannot manufacture mastery.**
///
/// The obvious way to build this is to let a passed rep top up the learner's mastery credit — after
/// all, they just proved they still know it. **PRAXIS does not**, and the Twin's projection is where
/// it is enforced: `OnRepAsync` writes belief, stability and difficulty, and passes `masteryCredit`
/// and `isHeld` **through unchanged**.
///
/// If a rep could confer mastery, then a learner could reach `HOLD` by answering an endless stream
/// of cheap questions about something they had already proved — and the Evidence Ceiling (C-4),
/// which is the load-bearing wall of this entire product, would have a door in it marked *"review"*.
///
/// A rep tells you the memory is **still there**. It does not tell you they can *do* anything, and
/// only the second thing is mastery.
///
/// **2. It does not get its own private grader.**
///
/// The rep goes to the same oracle as everything else (M5.3), gets back one of the same three words,
/// and the answer is in the same place it always is: **not here.** There is no `GET /reps/{id}/answer`
/// and no "just show me" button, because a review queue is exactly where such a button would feel
/// most reasonable and do the most damage.
///
/// **3. Its confidence is captured before the outcome, like all the others.**
///
/// A rep is the best calibration data in the product — the learner has a *strong prior* about
/// whether they still know something, and it is frequently wrong. That signal is only worth anything
/// if it is collected before they are told (C-6).
///
/// ## And what it *does* do
///
/// It goes on the log as `RepAnswered`, and the Twin recomputes stability from the memory model.
/// **The event carries `StabilityBefore`/`StabilityAfter` and the projection ignores both** — they
/// are the writer's *belief* at the time, useful for auditing a disagreement and worthless as truth.
/// Same rule as the Answer Budget (M6.1): a writer that can name its own price will eventually name
/// a generous one.
public static class AnswerRep
{
    public sealed record Command(
        Guid ConceptId,
        Guid ItemId,

        /// What they claim. **Their words are data**, handed to the oracle as a claim to check.
        string Claim,

        /// **Stated before they find out.** A rep is the sharpest calibration signal PRAXIS gets — a
        /// learner has a strong opinion about whether they still know something, and it is very often
        /// wrong. That is only measurable if it is collected first.
        double StatedConfidence,

        int Seconds);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ConceptId).NotEmpty();
            RuleFor(c => c.ItemId).NotEmpty();
            RuleFor(c => c.Claim).NotEmpty().MaximumLength(20_000);
            RuleFor(c => c.StatedConfidence).InclusiveBetween(0.0, 1.0);
            RuleFor(c => c.Seconds).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IMemorySnapshot _memory;
        private readonly IAiPlane _ai;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            IEventStore events,
            ProjectionRunner projections,
            IMemorySnapshot memory,
            IAiPlane ai,
            IUserContext user,
            IClock clock)
            => (_events, _projections, _memory, _ai, _user, _clock)
                = (events, projections, memory, ai, user, clock);

        public async Task<Result<RepAnsweredResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var held = await _memory.HeldByAsync(learner, ct);

            var rep = held.FirstOrDefault(h => h.ConceptId == command.ConceptId);

            if (rep is null)
            {
                // **You cannot review something you never proved.** That is not a rep, it is
                // learning, and it belongs to the plan.
                return Result<RepAnsweredResponse>.NotFound(
                    "You do not hold this concept, so there is nothing here to review. It is not "
                    + "something you are behind on — it is something you have not learned yet, and "
                    + "that is the plan's business, not the review queue's.");
            }

            var before = rep.State;

            // ── The same oracle as everything else ───────────────────────────
            var checkedClaim = await _ai.CheckClaimAsync(
                new CheckClaimRequest(
                    ItemId: command.ItemId.ToString(),
                    Claim: command.Claim,
                    LearnerId: learner.ToString(),
                    ConceptId: command.ConceptId.ToString()),
                ct);

            if (!checkedClaim.IsSuccess)
            {
                // **PRAXIS does not guess, and it does not default to WRONG.** A learner whose memory
                // was marked as lapsed because a service was down would have their whole schedule
                // rebuilt around a fiction, and would never find out why.
                return checkedClaim.CarryFailure<RepAnsweredResponse>();
            }

            var correct = checkedClaim.Value.Outcome is ClaimOutcome.Correct;

            // What the writer *believes* happened to the memory. The projection will recompute it
            // from the model and ignore these two fields entirely — they are here for the audit
            // trail, so a disagreement between the writer and the projection is visible rather than
            // silent.
            var after = Domain.Fsrs.Next(before, Domain.Grading.Of(correct), rep.DaysSinceReview);

            var appended = await _events.AppendAsync(
                learner,
                new RepAnswered(
                    command.ItemId,
                    correct,
                    command.StatedConfidence,
                    command.Seconds,
                    StabilityBefore: before.Stability,
                    StabilityAfter: after.Stability),
                _clock.UtcNow,
                command.ConceptId,
                ct);

            if (!appended.IsSuccess)
            {
                return appended.CarryFailure<RepAnsweredResponse>();
            }

            await _projections.CatchUpAsync(ct);

            var sure = command.StatedConfidence >= 0.70;

            return Result<RepAnsweredResponse>.Success(new RepAnsweredResponse(
                Correct: correct,

                // **The whole point of a rep, said out loud.**
                Says: (sure, correct) switch
                {
                    (true, true)
                        => "Still there, and you knew it was. Nothing to see here — which is exactly "
                           + "what a good rep looks like.",

                    (true, false)
                        => "You were sure, and it had gone. **This is the single most useful thing "
                           + "that happens in PRAXIS** — you have been walking around believing you "
                           + "held this, and building on it. Now you know.",

                    (false, true)
                        => "You had it after all, and you did not think you did. Stop revising this "
                           + "one — that time is yours (I-10).",

                    (false, false)
                        => "Gone, and you suspected as much. Knowing what you have lost is a "
                           + "completely healthy state, and it is where the repair starts.",
                },

                // **What this did NOT do.** Said in the response, because it is the thing a learner —
                // and a future maintainer — will most expect it to have done.
                MasteryUnchanged: true,

                DaysUntilNextRep: Math.Round(after.IntervalFor(TargetRetention), 1)));
        }
    }

    /// The retention PRAXIS schedules against. Matches the Twin's, because two engines disagreeing
    /// about when a memory is due would produce a queue that argued with itself.
    public const double TargetRetention = 0.90;
}

/// **Everything the learner is told after a rep** — and note what is not here.
///
/// There is no `Answer`, no `Correction`, and no `Explanation`. The handler never had one: the oracle
/// returns one of three words and the answer stays on the far side of that wire (M5.7). A review
/// queue is precisely where a *"just show me the answer"* button would feel most reasonable, which is
/// precisely why there is not one.
public sealed record RepAnsweredResponse(
    bool Correct,

    string Says,

    /// **Always true, and it is stated rather than assumed.** A passed rep proves the memory is still
    /// there. It does not prove they can *do* anything with it — and only the second thing is
    /// mastery (C-4).
    bool MasteryUnchanged,

    double DaysUntilNextRep);
