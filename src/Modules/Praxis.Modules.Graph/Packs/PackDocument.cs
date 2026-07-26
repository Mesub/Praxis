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

    /// The **named wrong beliefs** a learner might hold about this concept — the concept's declared
    /// misconception library (I-6). Each is a *bug report on a human mind*, not a restatement of an
    /// error: not "they got recall and precision mixed up" but "I think a higher recall always means
    /// a better retriever." A wrong belief you can teach against, build a CRITIQUE item against, and
    /// watch die.
    ///
    /// The AI extractor matches a learner's wrong answer against **this closed set first**, and only
    /// proposes something new when nothing here fits — which is the one thing that keeps the global
    /// library from filling with a hundred n=1 rephrasings of the same idea. A concept that declares
    /// a `contrasts` edge is announcing exactly where these live.
    public List<PackMisconception> Misconceptions { get; set; } = [];

    /// What would prove a learner understands this. Required — see <see cref="PackValidator"/>.
    public PackContract? Proof { get; set; }
}

/// One named wrong belief, as authored in the pack. Its `Key` is a **stable, global** identifier —
/// the same wrong idea in two learners must carry the same key, or it cannot be matched, named, or
/// mined at cohort scale. Its `Statement` is first person, because a misconception a learner does
/// not recognise as *theirs* is one they keep.
public sealed class PackMisconception
{
    public string Key { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
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

    /// The shape items of this evidence type take. Legal only on `Apply` — a model-graded
    /// explanation has no options to choose between. Absent means `Prose`, which is every pack
    /// authored before M17.
    public ItemFormat Format { get; set; } = ItemFormat.Prose;
}
