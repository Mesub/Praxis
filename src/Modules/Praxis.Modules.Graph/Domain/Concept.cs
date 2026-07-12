using Praxis.Shared;

namespace Praxis.Modules.Graph.Domain;

/// An atomic node of knowledge. The unit of the graph, and the unit of mastery.
///
/// A concept is something a learner can *hold* — and therefore something they can be asked to
/// prove, and something they can lose. If you cannot state what evidence would demonstrate it,
/// it is not a concept; it is a topic, and topics belong in prose.
public sealed class Concept : BaseEntity
{
    /// Stable, human-readable, and the key a domain pack refers to concepts by. Changing a slug
    /// orphans every edge and every item that points at it, so: don't.
    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    /// One or two sentences. Not a lesson — lessons are synthesised on demand (E17). This is what
    /// the concept *is*, for the compiler and for the learner's map of their own mind.
    public string Summary { get; private set; } = null!;

    /// Which domain pack owns this concept. Packs are the unit of authorship and versioning.
    public string Domain { get; private set; } = null!;

    /// The IRT difficulty prior, on the logit scale: roughly -3 (trivial) to +3 (brutal).
    ///
    /// It starts as an estimate — from the domain expert, or from a model — and is *replaced by
    /// measurement* once enough learners have responded (R-7). An estimate that never gets
    /// corrected by reality is just an opinion with a decimal point.
    public double DifficultyPrior { get; private set; }

    /// Minutes of focused work a median learner needs to first produce evidence for this. The
    /// Compiler's edge weight is built from it (M4.1), so a bad estimate here doesn't error —
    /// it quietly wastes someone's month. Treated as a prior, and corrected by cohort data.
    public int EstimatedMinutes { get; private set; }

    public bool IsActive { get; private set; } = true;

    private Concept() { }   // EF

    public static Concept Create(
        string slug,
        string title,
        string summary,
        string domain,
        double difficultyPrior,
        int estimatedMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        if (difficultyPrior is < -4 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(difficultyPrior), difficultyPrior,
                "Difficulty is on the IRT logit scale, roughly -3 to +3. A value outside -4..4 "
                + "means someone has confused it with a percentage.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(estimatedMinutes);

        return new Concept
        {
            Slug = slug.Trim().ToLowerInvariant(),
            Title = title.Trim(),
            Summary = summary.Trim(),
            Domain = domain.Trim().ToLowerInvariant(),
            DifficultyPrior = difficultyPrior,
            EstimatedMinutes = estimatedMinutes,
        };
    }

    /// Difficulty and duration are priors. This is how measurement replaces them.
    public void Recalibrate(double difficulty, int estimatedMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(estimatedMinutes);

        DifficultyPrior = difficulty;
        EstimatedMinutes = estimatedMinutes;
    }

    public void Retire() => IsActive = false;
}
