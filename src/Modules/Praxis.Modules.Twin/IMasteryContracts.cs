using Praxis.Shared;

namespace Praxis.Modules.Twin;

/// How the Twin asks what a concept accepts as proof, **without knowing the Graph exists**.
///
/// The Graph authors contracts; the Twin applies them. Neither module references the other — the
/// contract travels between them as a value (`MasteryContract`, in Shared), and this port is
/// implemented at the composition root.
///
/// A concept with no contract is not a concept, and the pack loader refuses to publish one. If this
/// returns null, the graph was written to by something that bypassed the gate — so the projection
/// refuses to guess, and says so.
public interface IMasteryContracts
{
    Task<MasteryContract?> ForConceptAsync(Guid conceptId, CancellationToken ct = default);
}
