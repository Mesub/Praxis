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

    /// **Evidence that can never confer mastery on its own** — the other side of the ceiling.
    ///
    /// ## This used to be called `IsModelGraded`, and that name was a lie waiting to be believed
    ///
    /// The *behaviour* was always right: `RECALL`, `EXPLAIN` and `CRITIQUE` are ceiling-bound. But
    /// the **name** claimed they are all graded by a language model, and `RECALL` is not — it is
    /// graded by an **exact key match**, deterministically, with no model involved anywhere.
    ///
    /// That is not a cosmetic problem. It is a trap. Sooner or later somebody reads
    /// `IsModelGraded(Recall) == true`, checks how recall is actually marked, finds a string
    /// comparison, and concludes — **correctly, on the evidence in front of them** — that the code
    /// is wrong. They change it to `false`. And in that instant `RECALL` becomes ceiling-free, and
    /// a learner can reach mastery by remembering things, which is the one thing CANON says a search
    /// engine can also do.
    ///
    /// **A well-meaning refactor would have gutted C-4**, and every test would still have passed,
    /// because the tests asserted the old lie.
    ///
    /// ## The two properties are orthogonal, and they must stay that way
    ///
    /// *How* evidence is graded (`GradingPath`) and *whether it can confer mastery* (this) are
    /// different questions with different answers:
    ///
    /// * `RECALL` — deterministically graded, **and still cannot confer mastery**, because it is
    ///   fakeable by *looking it up*, not by fooling a grader.
    /// * `EXPLAIN`, `CRITIQUE` — model-graded, and cannot confer mastery because a model's opinion
    ///   is exactly what C-4 refuses to trust.
    /// * `APPLY`, `BUILD`, `PREDICT` — can confer mastery, because each is checked by something a
    ///   model cannot talk its way past: a never-seen problem, a test suite, or reality itself.
    ///
    /// Conflating them is how *"well, RECALL is deterministic, so surely it counts"* eventually gets
    /// said out loud in a planning meeting.
    public static bool IsCeilingBound(this EvidenceType type)
        => !CanCrossTheHoldThreshold.Contains(type);
}
