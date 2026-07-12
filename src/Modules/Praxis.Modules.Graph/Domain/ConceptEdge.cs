using Praxis.Shared;

namespace Praxis.Modules.Graph.Domain;

/// How two concepts relate. Only one of these constrains the *order* a learner may take them in.
public enum EdgeKind
{
    /// B REQUIRES A: you cannot hold B without holding A first.
    ///
    /// **This is the only edge the Compiler treats as a dependency**, and therefore the only one
    /// that can create a cycle, and the only one that can waste a learner's month if it is wrong.
    /// A missing REQUIRES edge doesn't throw an error — it just quietly sends someone at a wall
    /// they were never equipped to climb (R-2).
    Requires = 1,

    /// Related, and useful to see nearby. Carries no ordering.
    Relates = 2,

    /// Frequently confused with each other. This is where misconceptions live, and the Assessment
    /// engine mines these deliberately: the best distractor is the neighbouring idea.
    Contrasts = 3,

    /// A is applied in the domain of B. The bridge from "I know it" to "I can use it".
    AppliesTo = 4,
}

public sealed class ConceptEdge : BaseEntity
{
    public Guid FromConceptId { get; private set; }
    public Guid ToConceptId { get; private set; }

    public EdgeKind Kind { get; private set; }

    /// How strongly. For REQUIRES: 1.0 means "absolutely cannot proceed", 0.5 means "will
    /// struggle without it". The Compiler weights the path by this.
    public double Strength { get; private set; }

    /// Where this edge came from. An edge a human authored and an edge a model guessed are not
    /// the same claim, and the graph should never pretend otherwise (R-2: LLM proposes, expert
    /// disposes, evidence decides).
    public EdgeProvenance Provenance { get; private set; }

    private ConceptEdge() { }   // EF

    public static ConceptEdge Create(
        Guid fromConceptId,
        Guid toConceptId,
        EdgeKind kind,
        double strength = 1.0,
        EdgeProvenance provenance = EdgeProvenance.Authored)
    {
        if (fromConceptId == toConceptId)
        {
            throw new ArgumentException(
                "A concept cannot depend on itself.", nameof(toConceptId));
        }

        if (strength is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strength), strength, "Edge strength is a weight in (0, 1].");
        }

        return new ConceptEdge
        {
            FromConceptId = fromConceptId,
            ToConceptId = toConceptId,
            Kind = kind,
            Strength = strength,
            Provenance = provenance,
        };
    }
}

public enum EdgeProvenance
{
    /// A domain expert said so. The strongest claim we have.
    Authored = 1,

    /// A model proposed it and no human has confirmed it. Usable, but never trusted as an
    /// authored edge is trusted.
    Inferred = 2,

    /// The cohort's failures implied it: learners who hold A and B still fail C at a rate the
    /// model cannot explain, so something is missing. This is the self-healing Graph, and it is
    /// the only kind of edge that was *corrected by reality* rather than asserted by a person.
    Discovered = 3,
}
