using Praxis.Shared;

namespace Praxis.Modules.Graph.Packs;

/// The on-disk shape of a domain pack: `packs/{name}/pack.yaml`.
///
/// A pack is the unit of authorship. It is what a domain expert writes, what a reviewer argues
/// with, and what eventually a third party will submit for certification (roadmap Phase 4). Its
/// format is therefore boring on purpose — YAML a human can read in a diff.
public sealed class PackDocument
{
    public string Domain { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Note { get; set; } = string.Empty;

    public List<PackConcept> Concepts { get; set; } = [];
}

public sealed class PackConcept
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;

    /// IRT logit scale, roughly -3 (trivial) to +3 (brutal).
    public double Difficulty { get; set; }

    public int Minutes { get; set; }

    /// The slugs this concept cannot be held without. **The only ordering claim in the format**,
    /// and the only one that can cycle or waste a learner's month.
    public List<string> Requires { get; set; } = [];

    /// Frequently confused with these. Where misconceptions live, and where the Assessment engine
    /// goes looking for its best distractors.
    public List<string> Contrasts { get; set; } = [];

    public List<string> Relates { get; set; } = [];

    public List<string> AppliesTo { get; set; } = [];

    /// What would prove a learner understands this. Required — see <see cref="PackValidator"/>.
    public PackContract? Proof { get; set; }
}

public sealed class PackContract
{
    /// The mastery credit at which the concept counts as held.
    public double Hold { get; set; } = 0.80;

    public List<PackEvidence> Evidence { get; set; } = [];
}

public sealed class PackEvidence
{
    public EvidenceType Type { get; set; }

    /// The score this evidence must reach to count at all.
    public double MinScore { get; set; } = 0.7;

    /// The most mastery this evidence type can ever contribute, however often it is produced.
    public double Max { get; set; }

    public int Count { get; set; } = 1;
}
