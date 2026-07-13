using FluentValidation;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.TeachBacks;

/// **Explain it in your own words — and a rubric, not the tutor, grades how well.**
///
/// This is the Feynman test as evidence (`EXPLAIN`), and it is the most fakeable evidence there is,
/// because a language model is doing the grading. So two things hold the line, and neither of them
/// lives in this handler:
///
/// 1. **The rubric is on the far side of the wire.** This handler sends the learner's words and the
///    *concept*, never a rubric — it does not have one to send (M11.7b). The grader resolves the
///    standard and returns a verdict.
/// 2. **The Evidence Ceiling is applied by the Twin, not here** (C-4). This handler records what
///    happened; it does not decide what it is *worth*. It appends an accepted-evidence event, and the
///    Twin's projection runs `MasteryCredit`, which caps model-graded evidence below the hold
///    threshold no matter how good the explanation was. A beautiful teach-back **raises belief and
///    sharpens the Twin. It cannot, alone, make a learner held** — and the code that enforces that is
///    somewhere this handler cannot reach.
///
/// The two events are kept distinct on purpose: an `ExplanationRecorded` (the raw fact that a thing
/// was said and scored) and an `EvidenceAccepted`/`EvidenceAbstained` (whether it counts as proof).
/// The kernel forbids filing one under the other — conflating them would quietly grant mastery for
/// having talked.
public static class RecordTeachBack
{
    public sealed record Command(Guid SessionId, Guid ConceptId, string Text, int Seconds);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();
            RuleFor(c => c.ConceptId).NotEmpty();

            RuleFor(c => c.Text)
                .NotEmpty()
                .WithMessage("There is nothing here to mark.")
                .MaximumLength(20_000);

            RuleFor(c => c.Seconds).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IAiPlane _ai;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            IEventStore events,
            ProjectionRunner projections,
            IAiPlane ai,
            IUserContext user,
            IClock clock)
            => (_events, _projections, _ai, _user, _clock) = (events, projections, ai, user, clock);

        public async Task<Result<TeachBackResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            // Typed teach-back: perfect capture confidence (voice/ASR is deferred, M7.6b). `Prepare`
            // strips the speech and keeps only the content the rubric should see — and refuses when,
            // once the filler is gone, there is nothing left to mark.
            var prepared = TeachBack.Prepare(new Transcript(command.Text, 1.0));

            if (!prepared.IsSuccess)
            {
                return prepared.CarryFailure<TeachBackResponse>();
            }

            var explanation = prepared.Value;

            // The grader resolves the rubric on the far side (M11.7b); the core never holds it, so
            // there is nothing answer-adjacent here for the tutor to reach.
            var graded = await _ai.GradeByConceptAsync(
                new GradeByConceptRequest(
                    explanation.Content,
                    learner.ToString(),
                    command.ConceptId.ToString()),
                ct);

            if (!graded.IsSuccess)
            {
                // The grader could not be reached. PRAXIS does not invent a score and write it into a
                // model of a person's mind on the strength of an outage.
                return graded.CarryFailure<TeachBackResponse>();
            }

            var grade = graded.Value;

            var facts = new EventBatch(learner).Add(
                new ExplanationRecorded(
                    command.SessionId,
                    explanation.Content,
                    grade.Score ?? 0.0,
                    command.Seconds),
                _clock.UtcNow,
                command.ConceptId);

            // Mapped from the outcome, and kept as its own event: `Pending` **abstains** — it never
            // silently becomes a pass (R-1). A scored outcome is accepted evidence at that score, and
            // the Twin's contract decides — via `MinScore` and the Evidence Ceiling — whether and how
            // much it counts.
            var evidenceId = Guid.CreateVersion7();

            if (grade.Outcome is GradeOutcome.Pending)
            {
                facts.Add(
                    new EvidenceAbstained(
                        evidenceId, EvidenceType.Explain, grade.Reason, grade.EscalatedToHuman),
                    _clock.UtcNow,
                    command.ConceptId);
            }
            else
            {
                facts.Add(
                    new EvidenceAccepted(
                        evidenceId, EvidenceType.Explain, grade.Score ?? 0.0,
                        TeachBackEvidence.RubricAssurance),
                    _clock.UtcNow,
                    command.ConceptId);
            }

            // A rubric can name a wrong belief the same way the oracle can. The keys come from the
            // concept's declared set on the far side; they are never free text.
            foreach (var key in grade.Misconceptions.Where(k => k.Length > 0))
            {
                facts.Add(
                    new MisconceptionDetected(Guid.CreateVersion7(), key, Evidence: explanation.Content),
                    _clock.UtcNow,
                    command.ConceptId);
            }

            // One use case, one append, one transaction.
            var appended = await _events.AppendAsync(learner, facts.Build(), expectedLastSequence: null, ct);

            if (!appended.IsSuccess)
            {
                return appended.CarryFailure<TeachBackResponse>();
            }

            await _projections.CatchUpAsync(ct);

            return Result<TeachBackResponse>.Success(new TeachBackResponse(
                Outcome: grade.Outcome.ToString(),
                Score: grade.Score,
                Misconceptions: grade.Misconceptions,
                RaisedBelief: TeachBackEvidence.IsAccepted(grade.Outcome),
                Ceiling: TeachBackEvidence.CeilingNote));
        }
    }
}

/// The disposition rule and the honest ceiling note, pure — so the rule is testable on its own and
/// the wording lives in exactly one place.
public static class TeachBackEvidence
{
    /// A language model graded this, so trust it this much. The Evidence Ceiling (C-4) caps
    /// model-graded evidence below the hold threshold in the Twin regardless of assurance, so this
    /// only scales the contribution *within* that cap — it can never buy a way over the line.
    public const double RubricAssurance = 0.85;

    /// Said to the learner, always, on a good teach-back — so the ceiling is never a surprise.
    public const string CeilingNote =
        "A rubric graded this, so it can sharpen what PRAXIS believes about you and surface "
        + "misconceptions — but a language model's opinion can never, by itself, make you *held* "
        + "(the Evidence Ceiling). Only applying it, building with it, or predicting against reality "
        + "crosses that line.";

    /// `Pending` abstains — an honest "I am not sure this counts" is never rewritten as a pass (R-1).
    /// Anything with a score is accepted as evidence *at that score*; the contract decides its worth.
    public static bool IsAccepted(GradeOutcome outcome) => outcome is not GradeOutcome.Pending;
}

/// **What the learner is told** — the verdict on their explanation, and the ceiling on what it can do.
public sealed record TeachBackResponse(
    string Outcome,
    double? Score,
    IReadOnlyList<string> Misconceptions,

    /// True when the explanation was scored (not `Pending`) — it moved belief, subject to the ceiling.
    bool RaisedBelief,

    string Ceiling);
