namespace Praxis.Shared;

/// The six kinds of proof (I-2). Core domain language — the Graph names them in its contracts,
/// the Assessment engine produces them, the Twin weighs them, the Ledger records them.
///
/// **They are not equal, and the difference is the product.**
public enum EvidenceType
{
    /// You remembered it. The weakest thing a person can demonstrate.
    Recall = 1,

    /// You solved a problem you had never seen. A model cannot fabricate this on your behalf.
    Apply = 2,

    /// You explained it, and a rubric graded the explanation. The most subjective evidence there
    /// is, and — because a language model is doing the grading — the most fakeable.
    Explain = 3,

    /// You built something and it ran against hidden tests. It passed or it did not; no model's
    /// opinion is involved.
    Build = 4,

    /// You found the flaw in a wrong worked example.
    Critique = 5,

    /// You predicted the world, and the world settled it. The least fakeable evidence that exists.
    Predict = 6,
}

public static class EvidenceTypes
{
    /// **The Evidence Ceiling (CANON C-4), as a set.**
    ///
    /// Only these three can push a concept into HELD. Each is graded by something a language model
    /// cannot talk its way past: a never-seen problem, a test suite, or reality itself.
    ///
    /// Everything else — RECALL, EXPLAIN, CRITIQUE — can raise belief but is *structurally
    /// incapable* of crossing the threshold on its own, no matter how many times it is produced or
    /// how highly it scores. This is why a hallucinating grader can never manufacture mastery, and
    /// it is enforced when a PoM contract is constructed, not by asking a model to behave.
    public static readonly IReadOnlySet<EvidenceType> CanCrossTheHoldThreshold =
        new HashSet<EvidenceType>
        {
            EvidenceType.Apply,
            EvidenceType.Build,
            EvidenceType.Predict,
        };

    /// Evidence whose verdict comes from a language model's judgement, and which therefore
    /// inherits every one of that model's failure modes.
    public static bool IsModelGraded(this EvidenceType type)
        => !CanCrossTheHoldThreshold.Contains(type);
}
