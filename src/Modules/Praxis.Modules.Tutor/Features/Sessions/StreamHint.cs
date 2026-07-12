using Microsoft.AspNetCore.Http;
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

/// **The same hint, streamed — and every gate fires before a single byte leaves.**
///
/// This is the ordering that matters, and it is easy to get wrong. A streaming endpoint is tempting
/// to write as *"open the stream, then work out whether we should have"* — because that is what makes
/// the first token arrive quickly, and first-token latency is the only thing anybody measures about
/// a stream.
///
/// It would be a hole straight through the product. The ladder, the budget and the event log all
/// fire **first**, and the response only becomes a stream once they have all said yes. A learner
/// cannot get a free hint by asking for a streamed one, and cannot skip a rung by asking for it
/// quickly.
///
/// ## And the thing a stream takes away
///
/// Every other guarantee in PRAXIS is enforced by a **type** — the verdict has no field for a
/// sentence, the item has no field for an answer, the contract has no field for a key. **A stream
/// has no fields at all.** It is free text going straight to a browser, and nothing downstream can
/// inspect it.
///
/// So the only thing standing here is that the answer is not in the tutor's context. It is enough —
/// but it is the *only* thing, which is why the jailbreak suite is re-run against the stream (M6.5b)
/// rather than assumed to carry over from the buffered path.
public static class StreamHint
{
    public sealed record Command(Guid SessionId, KernelHintRung Rung, string? LearnerSaid);

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

        /// Returns a **failure** when a gate refuses — and the caller must not have opened a stream
        /// yet. It returns the token source only once everything has said yes.
        public async Task<Result<IAsyncEnumerable<string>>> HandleAsync(
            Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var session = await _db.Sessions
                .FirstOrDefaultAsync(s => s.Id == command.SessionId && s.LearnerId == learner, ct);

            if (session is null)
            {
                return Result<IAsyncEnumerable<string>>.NotFound("No such session.");
            }

            if (session.State is not SessionState.Open)
            {
                return Result<IAsyncEnumerable<string>>.Conflict(
                    $"This session is already {session.State}.");
            }

            // ── The learner spoke (LR-3) ─────────────────────────────────────
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

            // ── The ladder, BEFORE any byte is streamed ──────────────────────
            var considered = HintLadder.Consider(command.Rung, session.Ladder, budget);

            if (!considered.IsSuccess)
            {
                if (HintLadder.IsExhaustion(considered))
                {
                    await _events.AppendAsync(
                        learner,
                        new AnswerBudgetExhausted(
                            session.Id, budget.DeepestRung ?? KernelHintRung.Orient, budget.HintsTaken),
                        _clock.UtcNow,
                        session.ConceptId,
                        ct);

                    await _projections.CatchUpAsync(ct);

                    return Result<IAsyncEnumerable<string>>.Forbidden(considered.Message!);
                }

                return Result<IAsyncEnumerable<string>>.Invalid(considered.Message!);
            }

            var grant = considered.Value;

            // ── Pay, BEFORE any byte is streamed ─────────────────────────────
            //
            // A learner cannot get a hint they never paid for by asking for a streamed one and then
            // killing the connection.
            var spent = await _events.AppendAsync(
                learner,
                new HintTaken(session.Id, grant.Rung, budget.Remaining - grant.Cost),
                _clock.UtcNow,
                session.ConceptId,
                ct);

            if (!spent.IsSuccess)
            {
                return Result<IAsyncEnumerable<string>>.Invalid(spent.Message!);
            }

            await _projections.CatchUpAsync(ct);

            // ── Only now does anything become a stream ───────────────────────
            var tokens = _ai.TutorTurnStreamAsync(
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

            return Result<IAsyncEnumerable<string>>.Success(tokens);
        }
    }
}

/// Writing server-sent events to a browser.
public static class ServerSentEvents
{
    public static async Task WriteAsync(
        HttpResponse response, IAsyncEnumerable<string> tokens, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var token in tokens.WithCancellation(ct))
        {
            await response.WriteAsync(
                $"data: {System.Text.Json.JsonSerializer.Serialize(new { text = token })}\n\n", ct);

            await response.Body.FlushAsync(ct);
        }

        await response.WriteAsync("data: [DONE]\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
