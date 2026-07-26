using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **An item, as the core is allowed to know it.**
///
/// Read the fields. Then read them again looking for the answer, and notice that there isn't one.
///
/// This is not an omission and it is not a `[NotMapped]` property that happens to be `null` today.
/// **There is no column.** The answer went to the oracle's store on the far side of the AI-plane wire
/// the moment the item was written (M5.5), and the only thing that has ever come back out of there
/// is one of three words: *correct*, *partial*, *wrong*.
///
/// ## Why this is a table and not a comment
///
/// Every guarantee in PRAXIS that lives only in a prompt is a guarantee that survives until someone
/// rewrites the prompt. The tutor's inability to reveal the answer (C-5) is the product's central
/// claim, and it holds for exactly one reason: **the .NET process that serves the tutor has no
/// answer in it.** Not hidden. Not access-controlled. *Absent.*
///
/// A jailbreak retrieves nothing because there is nothing there. An SQL injection into this schema
/// retrieves nothing because there is nothing there. A rogue employee with production read access
/// retrieves nothing, and `SchemaTests` reads `information_schema` — the **real** database, not this
/// C# type — and fails the build if anybody ever adds a column that looks like one.
public sealed class BankedItem
{
    public Guid Id { get; private set; }

    public Guid ConceptId { get; private set; }

    public EvidenceType EvidenceType { get; private set; }

    /// The question. **The learner can see this** — it is not a secret, and never was. The secret is
    /// what the answer to it is, and that is somewhere else entirely.
    public string Stem { get; private set; } = string.Empty;

    /// The shape of this item, and therefore how it is marked. `Prose` is every item banked before
    /// M17 and its marking is unchanged.
    public ItemFormat Format { get; private set; } = ItemFormat.Prose;

    /// The text under study, for a reading item. **The learner reads it** — it is no more secret
    /// than the stem, and the tutor is given it so a hint can point at an actual sentence.
    public string? Passage { get; private set; }

    /// The choices, in the order they are shown. **Not a secret**: they are on the screen.
    ///
    /// Which one is correct is not here, is not nullable-and-empty here, and is not derivable from
    /// here. It is a single integer in the oracle's own database, and the only thing that has ever
    /// come back out of that database is one of three words.
    public IReadOnlyList<string>? Options { get; private set; }

    /// A stable hash of the stem. The core remembers these and passes them back as `AlreadySeen`,
    /// which is how *"never seen before"* stays a fact rather than a hope (M5.5).
    public string Fingerprint { get; private set; } = string.Empty;

    /// IRT discrimination — how sharply this item separates those who hold the concept from those
    /// who do not. A low `a` is an item that everyone half-passes; it tells you nothing and it costs
    /// a learner the same minute as a good one.
    public double Discrimination { get; private set; } = 1.0;

    /// IRT difficulty. Seeded from an LLM prior blended with structural features, then earned from
    /// data (M7.2).
    public double Difficulty { get; private set; }

    /// The 3PL guessing floor. Non-zero on multiple choice, because a coin has a `θ` too.
    public double Guessing { get; private set; }

    /// The family this item was drawn from — its siblings share a difficulty estimate until there is
    /// enough data to tell them apart.
    public Guid? FamilyId { get; private set; }

    /// **The wrong belief this item's distractors are built to catch**, by key, from the concept's
    /// declared set. Never free text.
    ///
    /// This is the field that makes I-6 possible: when a learner is carrying a named misconception,
    /// the selector can go looking for the items that *diagnose that specific belief* rather than
    /// items that are merely hard. It is the difference between hunting and grinding.
    public string? TargetsMisconception { get; private set; }

    /// The name the oracle filed this item's key under; null for items whose keys were seeded by
    /// other means.
    public string? OracleId { get; private set; }

    /// Sympson–Hetter exposure. **An item everybody sees is an item everybody has heard about.**
    public int TimesServed { get; private set; }

    public int TimesCorrect { get; private set; }

    private BankedItem() { }

    public static BankedItem From(
        Guid id,
        Guid conceptId,
        EvidenceType evidenceType,
        string stem,
        string fingerprint,
        double difficulty,
        double discrimination = 1.0,
        double guessing = 0.0,
        Guid? familyId = null,
        string? targetsMisconception = null,
        string? oracleId = null,
        ItemFormat format = ItemFormat.Prose,
        string? passage = null,
        IReadOnlyList<string>? options = null)
        => new()
        {
            Id = id,
            ConceptId = conceptId,
            EvidenceType = evidenceType,
            Stem = stem,
            Fingerprint = fingerprint,
            Difficulty = difficulty,
            Discrimination = discrimination,
            Guessing = guessing,
            FamilyId = familyId,
            TargetsMisconception = targetsMisconception,
            OracleId = oracleId,
            Format = format,
            Passage = passage,
            Options = options,
        };

    /// Only a projection may call this, and only in response to an event that says it happened.
    internal void Observe(bool correct)
    {
        TimesServed++;

        if (correct)
        {
            TimesCorrect++;
        }
    }

    /// The item's IRT parameters, as the estimator wants them.
    public Response AsResponse(bool correct)
        => new(correct, Discrimination, Difficulty, Guessing);
}
