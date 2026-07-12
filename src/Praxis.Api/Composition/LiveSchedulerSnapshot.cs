using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Compiler;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Scheduler;
using Praxis.Modules.Scheduler.Domain;
using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Twin.Features.States;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Learning;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **Where what is rotting meets where they are going.**
///
/// The Twin knows what is slipping. The Compiler knows what is next. Neither knows the other exists,
/// and the Scheduler — which has to choose between them — knows neither. It is handed two lists and
/// returns one card.
///
/// Decay is applied *here*, at read time, exactly as the Twin's own API does (M3.7b) and the
/// Compiler's snapshot does (M4.4b). The stored state records what was proved and when; what the
/// learner can retrieve **today** is computed on the way out. A card scheduled against yesterday's
/// retrievability is a card for yesterday's person — and the whole reason PRAXIS exists is that
/// yesterday's person is not who sat down.
public sealed class LiveSchedulerSnapshot : ISchedulerSnapshot
{
    private readonly TwinDbContext _twin;
    private readonly GraphDbContext _graph;
    private readonly PlanService _plans;
    private readonly IClock _clock;

    public LiveSchedulerSnapshot(
        TwinDbContext twin,
        GraphDbContext graph,
        PlanService plans,
        IClock clock)
        => (_twin, _graph, _plans, _clock) = (twin, graph, plans, clock);

    public async Task<Result<SchedulerInputs>> ForLearnerAsync(
        Guid learnerId,
        int sessionMinutes,
        CancellationToken ct = default)
    {
        var compiled = await _plans.ForLearnerAsync(learnerId, ct);

        if (!compiled.IsSuccess)
        {
            // No objective, or a deadline in the past. Either way there is no "next", and PRAXIS
            // will not invent one — the refusal travels intact rather than becoming a blank card.
            return compiled.CarryFailure<SchedulerInputs>();
        }

        // ── What is rotting ──────────────────────────────────────────────────
        //
        // Only what they actually *hold*. A concept they never proved cannot be "due" — it is
        // simply not learned yet, and that is the plan's business, not the review queue's.
        var held = await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId && s.IsHeld)
            .ToListAsync(ct);

        var now = _clock.UtcNow;

        var due = new List<DueConcept>();

        foreach (var state in held)
        {
            var retrievability = RetrievabilityNow(state, now);

            if (retrievability >= LiveTwin.DueThreshold)
            {
                continue;   // Still fresh. Reviewing it now would teach them almost nothing (M3.3).
            }

            var concept = await _graph.Concepts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == state.ConceptId, ct);

            if (concept is null)
            {
                continue;   // Held against a concept no loaded pack contains. Not schedulable.
            }

            due.Add(new DueConcept(
                state.ConceptId,
                concept.Slug,
                retrievability,
                Minutes: RepMinutes(concept.EstimatedMinutes)));
        }

        // ── Where they are going ─────────────────────────────────────────────
        //
        // The plan already excludes everything they hold, so its first step *is* the next thing.
        // Null when the objective is reached — and "nothing" is a real card (I-10).
        var next = compiled.Value.Plan.Steps.Count > 0 ? compiled.Value.Plan.Steps[0] : null;

        return Result<SchedulerInputs>.Success(new SchedulerInputs(sessionMinutes, due, next));
    }

    /// A rep is not a re-teach. Retrieving something you already hold costs a fraction of learning
    /// it — and pricing it as a re-teach would make the Scheduler's rep cap fire constantly, so the
    /// learner would never review anything and would quietly lose the lot.
    private static int RepMinutes(int estimatedMinutes)
        => Math.Clamp((int)Math.Ceiling(estimatedMinutes * 0.10), 2, 10);

    /// How much of it they could actually retrieve **today** — not on the day they proved it.
    private static double RetrievabilityNow(ConceptState state, DateTime now)
    {
        if (state.Stability <= 0 || state.LastEvidenceAt is null)
        {
            return 0.0;
        }

        var elapsed = Math.Max(0.0, (now - state.LastEvidenceAt.Value).TotalDays);

        return Fsrs.Retrievability(state.Stability, elapsed);
    }
}
