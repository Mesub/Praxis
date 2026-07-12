using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Memory;
using Praxis.Modules.Memory.Domain;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **Where what the learner proved meets what forgetting has done to it.**
///
/// The Twin knows what was proved and when. E7 knows what time does to it. **Neither knows the other
/// exists** — no module reads another module's tables — so the adapter lives here, at the composition
/// root, where the application is assembled.
///
/// This is also what keeps the rep queue a pure function of plain values, which is what let M8.2
/// sweep the whole memory model in a unit test and *find* the sweet spot rather than assert a number
/// somebody liked the look of.
public sealed class LiveMemorySnapshot : IMemorySnapshot
{
    private readonly TwinDbContext _twin;
    private readonly GraphDbContext _graph;
    private readonly IClock _clock;

    public LiveMemorySnapshot(TwinDbContext twin, GraphDbContext graph, IClock clock)
        => (_twin, _graph, _clock) = (twin, graph, clock);

    public async Task<IReadOnlyList<DueRep>> HeldByAsync(Guid learnerId, CancellationToken ct = default)
    {
        // **Only what they hold.** A concept never proved cannot be "due" — it is not learned yet,
        // and a queue that pads itself with things you were never taught is a queue optimising for
        // looking busy.
        var held = await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId && s.IsHeld && s.Stability > 0)
            .ToListAsync(ct);

        if (held.Count == 0)
        {
            return [];
        }

        var conceptIds = held.Select(s => s.ConceptId).ToList();

        var slugs = await _graph.Concepts
            .AsNoTracking()
            .Where(c => conceptIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Slug, ct);

        var now = _clock.UtcNow;

        var reps = new List<DueRep>();

        foreach (var state in held)
        {
            if (!slugs.TryGetValue(state.ConceptId, out var slug))
            {
                // Held against a concept no loaded pack contains. Not schedulable — and PRAXIS will
                // not invent a name for it to keep the queue looking full.
                continue;
            }

            var elapsed = state.LastEvidenceAt is { } last
                ? Math.Max(0.0, (now - last).TotalDays)
                : 0.0;

            reps.Add(new DueRep(
                state.ConceptId,
                slug,
                new MemoryState(state.Stability, state.Difficulty),
                elapsed));
        }

        return reps;
    }
}
