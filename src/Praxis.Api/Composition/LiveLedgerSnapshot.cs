using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Ledger;
using Praxis.Modules.Ledger.Domain;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Composition;

/// **Where what the learner proved is turned into something they can sign and hand over.**
///
/// The Twin believes; the Ledger signs; the Graph names. **None reads another's tables** — so the
/// adapter lives here. Only *held* competencies come through, and each one is stamped with the
/// **strongest evidence that backs it**: a concept held on a BUILD that ran against hidden tests is
/// exported as `Execution` at full assurance, while one held on a rubric shows `Rubric` at its capped
/// assurance. That is deliberate — the record should say the *best* reason to trust a competency, and
/// carry that reason's honesty with it (C-6), not launder a model's opinion into certainty.
public sealed class LiveLedgerSnapshot : ILedgerSnapshot
{
    private readonly TwinDbContext _twin;
    private readonly GraphDbContext _graph;

    public LiveLedgerSnapshot(TwinDbContext twin, GraphDbContext graph)
        => (_twin, _graph) = (twin, graph);

    public async Task<IReadOnlyList<LedgerIssuer.Held>> HeldByAsync(
        Guid learnerId, CancellationToken ct = default)
    {
        var held = await _twin.States
            .AsNoTracking()
            .Where(s => s.LearnerId == learnerId && s.IsHeld)
            .OrderBy(s => s.ConceptId)
            .Select(s => s.ConceptId)
            .ToListAsync(ct);

        if (held.Count == 0)
        {
            return [];
        }

        var evidence = await _twin.Evidence
            .AsNoTracking()
            .Where(e => e.LearnerId == learnerId && held.Contains(e.ConceptId))
            .ToListAsync(ct);

        var names = await _graph.Concepts
            .AsNoTracking()
            .Where(c => held.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        var result = new List<LedgerIssuer.Held>(held.Count);
        var sequence = 1;

        foreach (var conceptId in held)
        {
            // The best reason to trust this competency: the piece of evidence PRAXIS is most sure of.
            var best = evidence
                .Where(e => e.ConceptId == conceptId)
                .OrderByDescending(e => e.Assurance)
                .ThenByDescending(e => e.Score)
                .FirstOrDefault();

            if (best is null)
            {
                // Held with no accepted evidence item is not something the belief update can produce,
                // so this would mean the Twin was written to outside the gate. Do not invent a
                // competency for it — a signed claim must trace to real evidence.
                continue;
            }

            result.Add(new LedgerIssuer.Held(
                conceptId,
                names.GetValueOrDefault(conceptId, conceptId.ToString()),
                best.Type,
                GradingMethodFor(best.Type),
                best.Score,
                best.Assurance,
                sequence++));
        }

        return result;
    }

    private static GradingMethod GradingMethodFor(EvidenceType type) => type switch
    {
        EvidenceType.Build => GradingMethod.Execution,   // ran against hidden tests
        EvidenceType.Apply => GradingMethod.KeyMatch,    // the oracle checked the answer
        EvidenceType.Recall => GradingMethod.KeyMatch,   // a keyed match, checked deterministically
        EvidenceType.Explain => GradingMethod.Rubric,    // a language model graded it — capped
        _ => GradingMethod.Rubric,
    };
}
