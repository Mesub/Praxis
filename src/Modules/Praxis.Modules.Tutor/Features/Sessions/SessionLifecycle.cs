using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Features.Sessions;

/// Starting a session, and the four ways it can end.
public static class StartSession
{
    /// `ItemId` is last and defaulted so every call site written before M17 keeps compiling
    /// unchanged. When it is null, behaviour is exactly what it was before: the tutor falls back to
    /// "the current problem", honestly, because there genuinely is no item.
    public sealed record Command(Guid ConceptId, int PlannedMinutes, Guid? ItemId = null);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ConceptId).NotEmpty();

            RuleFor(c => c.PlannedMinutes)
                .Must(m => m is 5 or 10 or 15 or 25)
                .WithMessage("A session is 5, 10, 15 or 25 minutes (M4.5).");
        }
    }

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            TutorDbContext db, IEventStore events, ProjectionRunner projections,
            IUserContext user, IClock clock)
            => (_db, _events, _projections, _user, _clock) = (db, events, projections, user, clock);

        public async Task<Result<SessionResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var sessionId = Guid.CreateVersion7();

            var started = await _events.AppendAsync(
                learner,
                new SessionStarted(sessionId, command.PlannedMinutes, command.ItemId),
                _clock.UtcNow,
                command.ConceptId,
                ct);

            if (!started.IsSuccess)
            {
                return Result<SessionResponse>.Invalid(started.Message!);
            }

            await _projections.CatchUpAsync(ct);

            return Result<SessionResponse>.Success(
                await Sessions.LoadAsync(_db, learner, sessionId, ct));
        }
    }
}

/// **The learner made something**, and it buys back budget (M6.2b).
public static class RecordProduction
{
    public sealed record Command(Guid SessionId, ProductionKind Kind);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();
            RuleFor(c => c.Kind).IsInEnum();
        }
    }

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            TutorDbContext db, IEventStore events, ProjectionRunner projections,
            IUserContext user, IClock clock)
            => (_db, _events, _projections, _user, _clock) = (db, events, projections, user, clock);

        public async Task<Result<SessionResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var session = await Sessions.OpenAsync(_db, learner, command.SessionId, ct);

            if (!session.IsSuccess)
            {
                return session.CarryFailure<SessionResponse>();
            }

            await _events.AppendAsync(
                learner,
                new ProductionRecorded(command.SessionId, command.Kind),
                _clock.UtcNow,
                session.Value.ConceptId,
                ct);

            await _projections.CatchUpAsync(ct);

            return Result<SessionResponse>.Success(
                await Sessions.LoadAsync(_db, learner, command.SessionId, ct));
        }
    }
}

/// *"Where, exactly, does your reasoning break?"*
///
/// **The most valuable two sentences in the session.** It is the only artefact in PRAXIS that says
/// *why* somebody is stuck rather than merely *that* they are — and it is misconception evidence
/// (I-6), never mastery evidence.
public static class WriteStuckNote
{
    public sealed record Command(Guid SessionId, string Note);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();

            RuleFor(c => c.Note)
                .NotEmpty()
                .MinimumLength(20)
                .WithMessage(
                    "A stuck note is two sentences about where your reasoning breaks. \"I don't "
                    + "know\" is not one of them — it says that you are stuck, and PRAXIS already "
                    + "knows that. What it needs is *where*.")
                .MaximumLength(4_000);
        }
    }

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            TutorDbContext db, IEventStore events, ProjectionRunner projections,
            IUserContext user, IClock clock)
            => (_db, _events, _projections, _user, _clock) = (db, events, projections, user, clock);

        public async Task<Result<SessionResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var session = await Sessions.OpenAsync(_db, learner, command.SessionId, ct);

            if (!session.IsSuccess)
            {
                return session.CarryFailure<SessionResponse>();
            }

            await _events.AppendAsync(
                learner,
                new StuckNoteWritten(command.SessionId, command.Note),
                _clock.UtcNow,
                session.Value.ConceptId,
                ct);

            await _projections.CatchUpAsync(ct);

            return Result<SessionResponse>.Success(
                await Sessions.LoadAsync(_db, learner, command.SessionId, ct));
        }
    }
}

/// **Closing it — and the refusal that stops it closing empty** (M6.4).
public static class EndSession
{
    public sealed record Command(Guid SessionId, bool Abandoned, int ActualMinutes);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SessionId).NotEmpty();
            RuleFor(c => c.ActualMinutes).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(
            TutorDbContext db, IEventStore events, ProjectionRunner projections,
            IUserContext user, IClock clock)
            => (_db, _events, _projections, _user, _clock) = (db, events, projections, user, clock);

        public async Task<Result<SessionResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var open = await Sessions.OpenAsync(_db, learner, command.SessionId, ct);

            if (!open.IsSuccess)
            {
                return open.CarryFailure<SessionResponse>();
            }

            var session = open.Value;

            if (command.Abandoned)
            {
                // **They left, and PRAXIS records it honestly** (I-10). No guilt notification, no
                // streak to save, no "you were so close!". The Motivation Engine gets to ask why.
                await _events.AppendAsync(
                    learner,
                    new SessionAbandoned(session.Id, command.ActualMinutes, AtStep: null),
                    _clock.UtcNow,
                    session.ConceptId,
                    ct);
            }
            else
            {
                // **The refusal.** A session in which the learner produced nothing cannot be
                // completed — and PRAXIS does not quietly downgrade it to abandoned and move on. It
                // says what it needs, and the learner's honest options are to make one thing, or to
                // say they are leaving. Both are true. "Completed" would not be.
                if (!session.MadeSomething)
                {
                    return Result<SessionResponse>.Invalid(
                        "This session cannot be completed, because you have not produced anything "
                        + "yet. You read, you were helped — and a session that ends in pure "
                        + "consumption teaches a person to mistake fluency for knowledge, which is "
                        + "the exact thing PRAXIS exists to stop. Make one thing: an attempt, an "
                        + "explanation, or an honest note about where you got stuck. Any of those "
                        + "will do. If you are leaving, say so, and PRAXIS will record that "
                        + "truthfully instead.");
                }

                await _events.AppendAsync(
                    learner,
                    new SessionCompleted(session.Id, command.ActualMinutes, session.Produced),
                    _clock.UtcNow,
                    session.ConceptId,
                    ct);
            }

            await _projections.CatchUpAsync(ct);

            return Result<SessionResponse>.Success(
                await Sessions.LoadAsync(_db, learner, command.SessionId, ct));
        }
    }
}

public static class GetSession
{
    public sealed record Query(Guid SessionId);

    public sealed class Handler
    {
        private readonly TutorDbContext _db;
        private readonly IUserContext _user;

        public Handler(TutorDbContext db, IUserContext user) => (_db, _user) = (db, user);

        public async Task<Result<SessionResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var exists = await _db.Sessions
                .AnyAsync(s => s.Id == query.SessionId && s.LearnerId == learner, ct);

            return exists
                ? Result<SessionResponse>.Success(
                    await Sessions.LoadAsync(_db, learner, query.SessionId, ct))
                : Result<SessionResponse>.NotFound("No such session.");
        }
    }
}

/// Shared loading. **A learner only ever sees their own session** — every query is filtered by the
/// learner on the token, and there is no `?learnerId=`.
internal static class Sessions
{
    public static async Task<Result<Session>> OpenAsync(
        TutorDbContext db, Guid learner, Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.LearnerId == learner, ct);

        if (session is null)
        {
            return Result<Session>.NotFound("No such session.");
        }

        return session.State is SessionState.Open
            ? Result<Session>.Success(session)
            : Result<Session>.Conflict($"This session is already {session.State}.");
    }

    public static async Task<SessionResponse> LoadAsync(
        TutorDbContext db, Guid learner, Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);

        var budget = await db.Budgets.AsNoTracking()
            .FirstOrDefaultAsync(b => b.LearnerId == learner && b.ConceptId == session.ConceptId, ct)
            ?? AnswerBudget.For(learner, session.ConceptId);

        return session.ToResponse(budget);
    }
}
