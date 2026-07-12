using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Persistence;

namespace Praxis.Modules.Tutor.Projections;

/// **The budget is a projection, and that is what makes it honest.**
///
/// It is not a counter somebody decrements. It is *derived*, by replaying the `HintTaken` events on
/// the log — which means it can be rebuilt from zero (M1.6), and which means the only way to change
/// what a learner has spent is to **append an event saying they spent it**.
///
/// There is no other door. No admin endpoint, no support tool, no migration that tops people up
/// after a bad week. If somebody wants to give a learner more help, they must write it into the
/// history of that learner's mind, where the Twin will see it, discount the evidence accordingly
/// (I-5), and the Ledger will record it forever.
///
/// **The event carries a `BudgetRemaining` and this projection ignores it.** That field is the
/// *writer's belief* at the time — useful for auditing a disagreement, worthless as truth. What is
/// left is recomputed here from what was actually spent, so a buggy tutor that claimed the learner
/// had 99 hints left cannot make it so.
public sealed class AnswerBudgetProjection : IProjection
{
    private readonly TutorDbContext _db;

    public AnswerBudgetProjection(TutorDbContext db) => _db = db;

    public string Name => "tutor.answer-budgets";

    public IReadOnlySet<LearningEventType> Handles { get; } = new HashSet<LearningEventType>
    {
        LearningEventType.HintTaken,
        LearningEventType.ProductionRecorded,
    };

    public async Task ApplyAsync(LearningEvent e, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.ConceptId is not { } conceptId)
        {
            // A hint about nothing. The writer is broken; the log is append-only, so we cannot fix
            // it — but we can decline to draw a conclusion from it.
            return;
        }

        var budget = await _db.Budgets
            .FirstOrDefaultAsync(b => b.LearnerId == e.LearnerId && b.ConceptId == conceptId, ct);

        if (budget is null)
        {
            budget = AnswerBudget.For(e.LearnerId, conceptId);

            _db.Budgets.Add(budget);
        }

        switch (e.Type)
        {
            case LearningEventType.HintTaken:
                // **The spend is re-derived, not trusted.** If it now overdraws — because the tutor
                // that wrote this event had a stale view — the spend is refused, and the log keeps
                // the event as the record of an attempt that should not have been granted.
                //
                // The alternative is to clamp the budget to zero and grant the hint anyway: the
                // worst of both worlds, where the learner got the help and the Ledger says they did
                // not.
                budget.Spend(EventCatalogue.Deserialize<HintTaken>(e).Rung);
                break;

            case LearningEventType.ProductionRecorded:
                // **The other half. "Hints cost. Production earns."**
                //
                // Same rule as above, in the other direction: the event says *what the learner
                // made*, and the value is derived here. A tutor that could name its own price would
                // eventually name a generous one.
                budget.Earn(EventCatalogue.Deserialize<ProductionRecorded>(e).Kind);
                break;

            default:
                return;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// A rebuild throws everything away and recomputes it from history. If this ever stops working,
    /// some belief about a learner is being kept somewhere that history cannot audit.
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _db.Budgets.ExecuteDeleteAsync(ct);
    }
}
