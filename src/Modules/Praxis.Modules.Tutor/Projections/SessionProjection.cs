using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;

namespace Praxis.Modules.Tutor.Projections;

/// The session, derived from the log rather than remembered in a variable.
///
/// **What the learner made is not a counter somebody increments.** It is the number of production
/// events on the chain, recomputed from history — so the claim *"you produced three things in this
/// session"* is auditable by replay, and a bug in the tutor cannot inflate it.
public sealed class SessionProjection : IProjection
{
    private readonly TutorDbContext _db;

    public SessionProjection(TutorDbContext db) => _db = db;

    public string Name => "tutor.sessions";

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.SessionStarted,
        LearningEventType.HintTaken,
        LearningEventType.ProductionRecorded,
        LearningEventType.StuckNoteWritten,
        LearningEventType.LearnerResponded,
        LearningEventType.SessionCompleted,
        LearningEventType.SessionAbandoned,
    };

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Type is LearningEventType.SessionStarted)
        {
            var started = EventCatalogue.Deserialize<SessionStarted>(e);

            if (await _db.Sessions.AnyAsync(s => s.Id == started.SessionId, ct))
            {
                return;   // replay of an event we have already applied
            }

            _db.Sessions.Add(Session.Start(
                started.SessionId,
                e.LearnerId,
                e.ConceptId ?? Guid.Empty,
                started.PlannedMinutes,
                started.ItemId));

            await _db.SaveChangesAsync(ct);

            return;
        }

        var sessionId = SessionIdOf(e);

        if (sessionId is null)
        {
            return;
        }

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null)
        {
            // An event about a session that never started. The log is append-only, so this cannot be
            // repaired — but we can decline to invent a session around it.
            return;
        }

        switch (e.Type)
        {
            case LearningEventType.HintTaken:
                session.HintServed(EventCatalogue.Deserialize<HintTaken>(e).Rung);
                break;

            case LearningEventType.LearnerResponded:
                // **LR-3.** The ball is back with the tutor.
                session.LearnerSpoke();
                break;

            case LearningEventType.ProductionRecorded:
                session.Made();
                break;

            case LearningEventType.StuckNoteWritten:
                // **They still made something.** The session did not end in consumption.
                session.WroteStuckNote();
                break;

            case LearningEventType.SessionCompleted:
                // The completion is *re-derived*, not trusted: if the log says a session completed
                // in which the learner produced nothing, the refusal fires here too, and the session
                // stays open rather than being recorded as a success that never happened.
                session.Complete(held: false, partial: false);
                break;

            case LearningEventType.SessionAbandoned:
                session.Abandon();
                break;

            default:
                return;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
        => await _db.Sessions.ExecuteDeleteAsync(ct);

    private static Guid? SessionIdOf(LearningEvent e) => EventCatalogue.Deserialize(e) switch
    {
        HintTaken hint => hint.SessionId,
        LearnerResponded said => said.SessionId,
        ProductionRecorded made => made.SessionId,
        StuckNoteWritten note => note.SessionId,
        SessionCompleted done => done.SessionId,
        SessionAbandoned left => left.SessionId,
        _ => null,
    };
}
