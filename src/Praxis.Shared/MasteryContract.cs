namespace Praxis.Shared;

/// **What a concept accepts as proof**, as a value the Twin can reason about without knowing the
/// Graph exists.
///
/// The Graph *authors* contracts and refuses to construct dishonest ones. The Twin *applies* them
/// and refuses to let a dishonest one through even if it somehow existed. Two engines, two
/// refusals, one guarantee — and neither depends on the other's internals.
public sealed record MasteryContract(
    double HoldThreshold,
    IReadOnlyList<EvidenceClause> Clauses)
{
    /// The most that evidence a language model graded could ever contribute.
    ///
    /// **Always strictly below `HoldThreshold`.** That is the Evidence Ceiling (CANON C-4), and it
    /// is the reason a hallucinating grader can inflate belief but never manufacture mastery.
    public double ModelGradedCeiling
        => Clauses.Where(c => c.Type.IsModelGraded()).Sum(c => c.MaxContribution);

    /// The most that evidence a model *cannot* fake could contribute.
    ///
    /// **Always at or above `HoldThreshold`.** That is the Floor: a learner who does every piece of
    /// real work the concept asks for must be able to hold it without any rubric's blessing.
    public double UnfakeableCeiling
        => Clauses.Where(c => !c.Type.IsModelGraded()).Sum(c => c.MaxContribution);

    public EvidenceClause? ClauseFor(EvidenceType type)
        => Clauses.FirstOrDefault(c => c.Type == type);

    public bool Accepts(EvidenceType type) => ClauseFor(type) is not null;
}

/// One clause: this kind of proof, at this standard, this many times, is worth at most this much.
public sealed record EvidenceClause(
    EvidenceType Type,
    double MinScore,
    double MaxContribution,
    int RequiredCount);
