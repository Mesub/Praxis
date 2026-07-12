using Praxis.Shared;

namespace Praxis.Modules.Graph.Domain;

/// **What this concept will accept as proof that you understand it** (I-2).
///
/// Every concept ships with one. It names which of the six evidence types count, how well you must
/// do on each, and how much mastery each contributes. Without a contract, a concept is a topic —
/// something to read about, with no way to ever be done reading.
///
/// ## The Evidence Ceiling, and why it lives here
///
/// A contract is **structurally incapable** of letting a learner reach HELD by talking to a
/// language model (CANON C-4). `Create` refuses to build one where the model-graded evidence types
/// — RECALL, EXPLAIN, CRITIQUE — can sum to the hold threshold.
///
/// This is deliberately not a runtime check on scores, and not an instruction in a prompt. It is
/// arithmetic, checked when the contract is authored: *if every model-graded rubric in the world
/// returned a perfect score, forever, the learner would still not hold the concept.* Crossing the
/// line requires evidence the model cannot fabricate — a never-seen problem solved, code that
/// actually ran, or a prediction reality resolved.
///
/// A hallucinating grader can therefore inflate belief. It can never manufacture mastery.
public sealed class PoMContract : BaseEntity
{
    public Guid ConceptId { get; private set; }

    /// The mastery credit at which the concept counts as HELD. Below it, the learner is working
    /// on it. Above it, they hold it — and it begins to decay from that moment.
    public double HoldThreshold { get; private set; }

    private readonly List<AcceptedEvidence> _accepted = [];
    public IReadOnlyList<AcceptedEvidence> Accepted => _accepted;

    private PoMContract() { }   // EF

    public static PoMContract Create(
        Guid conceptId,
        double holdThreshold,
        IReadOnlyList<AcceptedEvidence> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);

        if (holdThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(holdThreshold), holdThreshold,
                "The hold threshold is a mastery credit in (0, 1].");
        }

        if (accepted.Count == 0)
        {
            throw new ArgumentException(
                "A concept with no accepted evidence can never be held, and no learner could ever "
                + "finish it. If you cannot say what would prove it, it is a topic, not a concept.",
                nameof(accepted));
        }

        var duplicate = accepted
            .GroupBy(a => a.Type)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"{duplicate.Key} is listed twice. Each evidence type gets one clause.",
                nameof(accepted));
        }

        // Can this concept ever be held at all?
        if (!accepted.Any(a => EvidenceTypes.CanCrossTheHoldThreshold.Contains(a.Type)))
        {
            throw new ArgumentException(
                "This contract accepts only model-graded evidence, so no learner could ever hold "
                + "the concept — the Evidence Ceiling would cap them below the threshold forever. "
                + "Every concept needs at least one of APPLY, BUILD or PREDICT: a never-seen "
                + "problem, code that runs, or a prediction reality settles.",
                nameof(accepted));
        }

        // THE FLOOR, and it is the ceiling's other half.
        //
        // Unfakeable evidence must be *sufficient on its own* to reach the threshold. If it is not,
        // then the last step into HELD can only be taken with a language model's blessing — and the
        // ceiling's whole promise, that real work is what crosses the line, quietly inverts.
        //
        // Model-graded evidence may accelerate a learner. It may never be what completes them.
        var unfakeableCeiling = accepted
            .Where(a => EvidenceTypes.CanCrossTheHoldThreshold.Contains(a.Type))
            .Sum(a => a.MaxContribution);

        if (unfakeableCeiling < holdThreshold)
        {
            throw new ArgumentException(
                $"Evidence a model cannot fake ({string.Join(", ", accepted.Where(a => !a.Type.IsCeilingBound()).Select(a => a.Type))}) "
                + $"tops out at {unfakeableCeiling:F2}, below the hold threshold of {holdThreshold:F2}. "
                + "So a learner who did every piece of real work this concept asks for would still "
                + "need a rubric's opinion to be recorded as holding it. Real work must be enough. "
                + "Raise the contributions, or lower the threshold. (CANON C-4.)",
                nameof(accepted));
        }

        // THE CEILING. If every model-graded rubric returned a perfect score, forever, the learner
        // must still fall short of holding this concept.
        var modelGradedCeiling = accepted
            .Where(a => a.Type.IsCeilingBound())
            .Sum(a => a.MaxContribution);

        if (modelGradedCeiling >= holdThreshold)
        {
            throw new ArgumentException(
                $"Model-graded evidence ({string.Join(", ", accepted.Where(a => a.Type.IsCeilingBound()).Select(a => a.Type))}) "
                + $"can contribute up to {modelGradedCeiling:F2}, which reaches the hold threshold "
                + $"of {holdThreshold:F2}. That means a learner could be recorded as holding this "
                + "concept on the strength of a language model's opinion alone — and a "
                + "hallucinating grader would manufacture mastery. Lower the contributions, or "
                + "raise the threshold. (CANON C-4.)",
                nameof(accepted));
        }

        var contract = new PoMContract
        {
            ConceptId = conceptId,
            HoldThreshold = holdThreshold,
        };

        contract._accepted.AddRange(accepted);

        return contract;
    }

    /// The most mastery this contract could ever grant from evidence a model grades. Always
    /// strictly below <see cref="HoldThreshold"/> — that is the whole point.
    public double ModelGradedCeiling
        => _accepted.Where(a => a.Type.IsCeilingBound()).Sum(a => a.MaxContribution);

    public bool Accepts(EvidenceType type) => _accepted.Any(a => a.Type == type);

    public AcceptedEvidence? ClauseFor(EvidenceType type)
        => _accepted.FirstOrDefault(a => a.Type == type);
}

/// One clause of a contract: this kind of proof, at this standard, is worth this much.
public sealed class AcceptedEvidence : BaseEntity
{
    public Guid PoMContractId { get; private set; }

    public EvidenceType Type { get; private set; }

    /// The score this evidence must reach to count at all. Below it, the attempt is recorded —
    /// and it is *still* evidence, of a misconception.
    public double MinScore { get; private set; }

    /// The most mastery credit this evidence type can ever contribute, however many times the
    /// learner produces it. Ten perfect explanations are not a build that runs.
    public double MaxContribution { get; private set; }

    /// How many times it must be produced for the full contribution. One BUILD that passes is
    /// proof. One correct RECALL is a coin flip that landed well.
    public int RequiredCount { get; private set; }

    private AcceptedEvidence() { }   // EF

    public static AcceptedEvidence Of(
        EvidenceType type,
        double minScore,
        double maxContribution,
        int requiredCount = 1)
    {
        if (minScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minScore), minScore, "A score is in [0, 1].");
        }

        if (maxContribution is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxContribution), maxContribution,
                "A contribution is a mastery credit in (0, 1].");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredCount);

        return new AcceptedEvidence
        {
            Type = type,
            MinScore = minScore,
            MaxContribution = maxContribution,
            RequiredCount = requiredCount,
        };
    }
}
