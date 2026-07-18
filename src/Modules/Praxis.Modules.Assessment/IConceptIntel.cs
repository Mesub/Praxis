namespace Praxis.Modules.Assessment;

/// What the item generator needs to know about a concept, from whoever holds it.
/// A port, like the tutor's IMisconceptionLibrary (M13.2b-ii): Assessment names what it needs;
/// the composition root decides where it comes from; no module reads another module's tables.
public sealed record ConceptIntel(
    string Summary,
    /// The generator's difficulty scale is 0..1; the pack's prior is an IRT logit. Mapped here,
    /// once: (prior + 3) / 6, clamped.
    double DifficultyTarget,
    IReadOnlyDictionary<string, string> DeclaredMisconceptions);

public interface IConceptIntel
{
    /// Null when the Graph has never heard of the concept — the caller refuses, it does not invent.
    Task<ConceptIntel?> ForAsync(Guid conceptId, CancellationToken ct = default);
}
