using Praxis.Modules.Ledger.Domain;

namespace Praxis.Modules.Ledger;

/// **What the learner has proved, as the Twin knows it** — the seam between the Ledger (which signs)
/// and the Twin (which believes), assembled at the composition root because no module reads another's
/// tables. Only *held* competencies come through: a concept below the bar is not a competency, and
/// the Ledger will not sign a claim the Twin does not hold.
public interface ILedgerSnapshot
{
    Task<IReadOnlyList<LedgerIssuer.Held>> HeldByAsync(Guid learnerId, CancellationToken ct = default);
}
