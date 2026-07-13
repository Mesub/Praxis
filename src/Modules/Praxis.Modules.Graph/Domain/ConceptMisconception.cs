using Praxis.Shared;

namespace Praxis.Modules.Graph.Domain;

/// **A named wrong belief that lives on a concept — the concept's declared misconception library.**
///
/// This is the *canonical* misconception, authored in the pack and owned by the graph. It is not the
/// per-learner one (that is the Twin's `Misconception`, which records whether *this* person still
/// answers as though they hold it). This is the shared definition the whole system points at: the
/// stable `Key` that makes "the same wrong idea in two learners" one idea, minable at cohort scale,
/// and the first-person `Statement` a learner can read back in their own voice.
///
/// The AI extractor is handed a concept's keys as its **closed match set** (I-6): it matches a wrong
/// answer against these before it is ever allowed to invent a new one, which is the single thing that
/// keeps the global library from filling with a hundred rephrasings of the same belief.
public sealed class ConceptMisconception : BaseEntity
{
    public Guid ConceptId { get; private set; }

    /// Stable, kebab-case, and **globally unique across the pack**. Changing it orphans every
    /// learner-local misconception and every item that points at it, exactly like a slug.
    public string Key { get; private set; } = null!;

    /// The wrong belief, first person, as the learner would state it.
    public string Statement { get; private set; } = null!;

    private ConceptMisconception() { }   // EF

    public static ConceptMisconception Create(Guid conceptId, string key, string statement)
    {
        if (conceptId == Guid.Empty)
        {
            throw new ArgumentException("A misconception must belong to a concept.", nameof(conceptId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);

        return new ConceptMisconception
        {
            ConceptId = conceptId,
            Key = key.Trim().ToLowerInvariant(),
            Statement = statement.Trim(),
        };
    }
}
