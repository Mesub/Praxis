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
                Minutes: RepMinutes(concept.EstimatedMinutes, retrievability)));
        }

        // ── Where they are going ─────────────────────────────────────────────
        //
        // The plan already excludes everything they hold, so its first step *is* the next thing.
        // Null when the objective is reached — and "nothing" is a real card (I-10).
        var next = compiled.Value.Plan.Steps.Count > 0 ? compiled.Value.Plan.Steps[0] : null;

        return Result<SchedulerInputs>.Success(new SchedulerInputs(sessionMinutes, due, next));
    }

    /// **What this will actually cost — which depends on whether they are going to get it right.**
    ///
    /// A rep is not a re-teach: retrieving something you already hold costs a fraction of learning it,
    /// and pricing every rep as a re-teach would make the Scheduler's rep cap fire constantly, so the
    /// learner would never review anything and would quietly lose the lot.
    ///
    /// **But it is not free either, and it was being priced as though it were.**
    ///
    /// This method used to return `clamp(estimated × 0.10, 2, 10)` for *every* due concept — including
    /// ones at 30% retrievability, which the learner will **fail seven times out of ten**. A failed
    /// rep is not a three-minute review. It is a lapse: the memory collapses and they spend the
    /// evening relearning something they thought they knew. Quoting three minutes for that is not an
    /// estimation error — **it is a false statement about how their evening is going to go**, made by
    /// the code that builds their day.
    ///
    /// So the price is the **expected** one, blended across passing and failing it (M8.2). The
    /// blending weight comes from E7, which owns the question *"what is a rep actually worth?"* — this
    /// adapter just has to stop lying about the bill.
    private static int RepMinutes(int estimatedMinutes, double retrievability)
    {
        var whenPassed = Math.Clamp(estimatedMinutes * 0.10, 2.0, 10.0);

        // A failure is a re-learn, and a re-learn is priced as one.
        var whenFailed = Math.Max(whenPassed, estimatedMinutes * 0.60);

        var r = Math.Clamp(retrievability, 0.0, 1.0);

        var expected = (r * whenPassed) + ((1.0 - r) * whenFailed);

        return Math.Max(2, (int)Math.Ceiling(expected));
    }

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
