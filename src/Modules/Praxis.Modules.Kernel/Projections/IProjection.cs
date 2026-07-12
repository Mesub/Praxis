using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Kernel.Projections;

/// A derived view of the event log.
///
/// The Twin is one of these. So is the rep queue, and the Ledger's index, and every analytic.
/// None of them is a source of truth — all of them are *opinions computed from history*, and
/// all of them must be reconstructible by replaying that history from zero.
///
/// The contract that matters: <see cref="ApplyAsync"/> must be a pure function of the events it
/// has seen. If a projection reads the wall clock, calls an LLM, or fetches something over the
/// network, then replaying the same log twice produces two different answers — and the moment
/// that is true, the Twin is no longer a projection, it is a rumour with a database table.
public interface IProjection
{
    /// Stable across deploys — it is the key the checkpoint is stored under. Renaming this
    /// silently orphans the checkpoint and the projection replays from zero.
    string Name { get; }

    /// Which events this projection cares about. Everything else is skipped without deserialising.
    IReadOnlySet<LearningEventType> Handles { get; }

    Task ApplyAsync(LearningEvent e, CancellationToken ct = default);

    /// Throw everything away. Called before a rebuild — the runner then replays the whole log
    /// into a projection that must behave as though it had never seen an event.
    Task ResetAsync(CancellationToken ct = default);
}
