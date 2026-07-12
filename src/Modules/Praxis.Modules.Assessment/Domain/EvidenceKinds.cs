using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **The six kinds of proof, as a table you can read** (CANON §1).
///
/// Everything about an evidence type lives here: what it proves, how a learner produces it, how it
/// is graded, what it is worth, and how hard it moves the Twin's belief. One table, one place to
/// argue with.
///
/// ## The two numbers, and why they are different
///
/// **Weight (`w`)** is how much of a concept's mastery this type can account for. The six sum to
/// exactly 1.00, and that is enforced by a test — a set of weights that does not sum to one produces
/// a mastery score that means nothing and still looks like a number.
///
/// **Update gain (`κ`)** is how *hard* one piece of evidence moves the Twin's belief. It is not the
/// same thing. `BUILD` is worth 0.25 of a concept but has a gain of 1.40: one build that ran tells
/// you a great deal, *right now*, about whether this person can do the thing. `RECALL` is worth 0.10
/// and has a gain of 0.35: twenty correct recalls should still leave you genuinely unsure.
///
/// ## Why RECALL is deliberately the weakest
///
/// > **It is the only one of the six that a search engine can also do.**
///
/// A product that weights recall highly is not measuring a person. It is measuring their access to a
/// lookup — and every learner who has ever crammed knows the difference, even if the dashboard does
/// not.
///
/// ## Why PREDICT cannot be cheated
///
/// **The answer does not exist yet.** There is no key to leak, no bank to share, no model to ask.
/// You cannot Google next Thursday. It is the only evidence type in the world with that property,
/// and it is why the Reality Loop exists at all.
public static class EvidenceKinds
{
    private static readonly Dictionary<EvidenceType, EvidenceKind> Table = new()
    {
        [EvidenceType.Recall] = new(
            Type: EvidenceType.Recall,
            Proves: "The item is retrievable. Nothing more.",
            Intake: Intake.KeyedAnswer,
            Grading: GradingPath.KeyMatch,
            Weight: 0.10,
            UpdateGain: 0.35,
            GamedBy: "Google it. Ask a model. Share the bank.",
            StoppedBy:
                "Timed windows, never-seen generation — and, mostly, by simply not weighting it. "
                + "**Design-level immunity**: there is nothing to gain by cheating at the thing that "
                + "is worth the least."),

        [EvidenceType.Apply] = new(
            Type: EvidenceType.Apply,
            Proves: "Transfer. The model in their head works on an instance they have never seen.",
            Intake: Intake.WorkedSolution,
            Grading: GradingPath.KeyMatch,
            Weight: 0.20,
            UpdateGain: 0.80,
            GamedBy: "Recognise the template from a worked example and pattern-match it.",
            StoppedBy:
                "The item is generated from the concept's **deep structure** with the surface "
                + "features randomised, and it is hashed for novelty (M5.5). An APPLY item the "
                + "learner has seen before is a RECALL item with a false label on it."),

        [EvidenceType.Explain] = new(
            Type: EvidenceType.Explain,
            Proves: "A causal, mechanistic model exists — not a memorised sentence (Feynman).",
            Intake: Intake.Explanation,
            Grading: GradingPath.Rubric,
            Weight: 0.20,
            UpdateGain: 0.90,
            GamedBy: "Have a language model write the explanation.",
            StoppedBy:
                "**The Evidence Ceiling** (C-4). It is not stopped — it is *contained*. No quantity "
                + "of perfect explanations can cross the hold threshold, so an outsourced one buys "
                + "the learner belief they cannot spend."),

        [EvidenceType.Build] = new(
            Type: EvidenceType.Build,
            Proves: "**The thing actually works.** Not an opinion — an execution.",
            Intake: Intake.Artifact,
            Grading: GradingPath.Execution,
            Weight: 0.25,
            UpdateGain: 1.40,
            GamedBy: "Have a model write the code.",
            StoppedBy:
                "Hidden tests run it. **A rubric can never override a failing test** — where truth "
                + "is deterministic, PRAXIS does not ask a language model. (And a learner who can "
                + "get a model to produce working code against hidden tests they cannot see has, "
                + "in fact, demonstrated something.)"),

        [EvidenceType.Critique] = new(
            Type: EvidenceType.Critique,
            Proves: "Discrimination. They can find the flaw in someone *else's* work.",
            Intake: Intake.Critique,
            Grading: GradingPath.Rubric,
            Weight: 0.10,
            UpdateGain: 0.60,
            GamedBy: "Guess that something is wrong somewhere and say so vaguely.",
            StoppedBy:
                "The flaw must be **localised, named, and repaired**. All three. Two out of three "
                + "is not a critique — it is a hunch."),

        [EvidenceType.Predict] = new(
            Type: EvidenceType.Predict,
            Proves: "A calibrated model of reality (I-3).",
            Intake: Intake.Prediction,
            Grading: GradingPath.Reality,
            Weight: 0.15,
            UpdateGain: 1.10,
            GamedBy: "**It cannot be.**",
            StoppedBy:
                "**The answer does not exist yet.** There is no key to leak, no bank to share, and "
                + "no model to ask. You cannot Google next Thursday. It is the only evidence type "
                + "in the world that is structurally impossible to cheat, and it is why the Reality "
                + "Loop exists."),
    };

    public static EvidenceKind Of(EvidenceType type)
        => Table.TryGetValue(type, out var kind)
            ? kind
            : throw new ArgumentOutOfRangeException(
                nameof(type), type,
                "An evidence type with no entry in the table has no weight, no grading path and no "
                + "meaning. It would be produced, recorded, and silently ignored by everything that "
                + "matters.");

    public static IReadOnlyCollection<EvidenceKind> All => Table.Values;

    /// The six weights sum to exactly 1.00. Checked at startup and by a test, because a set of
    /// weights that does not sum to one produces a number that means nothing and still looks like a
    /// score.
    public static double TotalWeight => Table.Values.Sum(k => k.Weight);
}

/// One evidence type, fully described.
public sealed record EvidenceKind(
    EvidenceType Type,

    /// What it actually demonstrates — and, just as importantly, what it does not.
    string Proves,

    /// What the learner submits.
    Intake Intake,

    /// How the verdict is reached. **This is not the same question as whether it can confer
    /// mastery** — see `IsCeilingBound`.
    GradingPath Grading,

    /// How much of a concept's mastery this type can account for. The six sum to 1.00.
    double Weight,

    /// How hard one piece of this evidence moves the Twin's belief. **Not the same as weight**: a
    /// BUILD is worth 0.25 of a concept but moves belief by 1.40, because one build that ran tells
    /// you a great deal *right now*.
    double UpdateGain,

    /// The honest account of how a learner would cheat at this.
    string GamedBy,

    /// And what actually stops them — which for RECALL is *"we do not weight it much"*, and for
    /// PREDICT is *"the answer does not exist yet"*.
    string StoppedBy)
{
    /// **Whether this can confer mastery on its own** (C-4).
    ///
    /// Note that this is *not* derivable from `Grading`, and that is the whole point. `RECALL` is
    /// graded deterministically and is **still** ceiling-bound — because it is fakeable by looking
    /// it up, not by fooling a grader.
    public bool IsCeilingBound => Type.IsCeilingBound();

    public bool CanConferMastery => !IsCeilingBound;
}

/// What the learner actually hands over.
public enum Intake
{
    /// A selected option, or a typed string matched against a key.
    KeyedAnswer = 1,

    /// Their working on a problem they have never seen.
    WorkedSolution = 2,

    /// Prose, or a teach-back to a named naive audience.
    Explanation = 3,

    /// Something that runs. Code, a spreadsheet, a model, a diagram with structure.
    Artifact = 4,

    /// A flaw, localised, named, and repaired. All three.
    Critique = 5,

    /// A falsifiable claim about the world, with a stated confidence, made **before** the world
    /// settles it. That ordering is the entire point (C-6).
    Prediction = 6,
}

/// **How the verdict is reached** — and note that a language model is involved in only two of them.
///
/// > *Where truth is deterministic, we use determinism.* An LLM judge is a tool for grading
/// > **arguments**, not for grading **arithmetic**. Half of the AI-grading disasters in this industry
/// > come from asking a language model to check a sum.
public enum GradingPath
{
    /// Exact or fuzzy match against a key. No model. No opinion.
    KeyMatch = 1,

    /// Multi-judge rubric with abstention (M5.4). A model's opinion — which is exactly why the
    /// evidence it grades is ceiling-bound.
    Rubric = 2,

    /// **It ran, or it did not.** Hidden tests execute. A rubric can never override a failing test.
    Execution = 3,

    /// **Reality settled it.** The world resolved the prediction, and the Brier score fell out.
    /// There is no appeal, and there is nobody to argue with.
    Reality = 4,
}
