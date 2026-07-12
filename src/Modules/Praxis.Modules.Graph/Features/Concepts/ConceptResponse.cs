using System.Linq.Expressions;
using Praxis.Modules.Graph.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

public sealed record ConceptResponse(
    Guid Id,
    string Slug,
    string Title,
    string Summary,
    string Domain,
    double DifficultyPrior,
    int EstimatedMinutes)
{
    // Projected in SQL by the read slices — only the columns we need leave the database.
    public static readonly Expression<Func<Concept, ConceptResponse>> Projection =
        c => new ConceptResponse(
            c.Id, c.Slug, c.Title, c.Summary, c.Domain, c.DifficultyPrior, c.EstimatedMinutes);
}

/// A concept plus what it would take to prove it. The learner is entitled to know the standard
/// they are being held to *before* they are held to it.
public sealed record ConceptDetailResponse(
    ConceptResponse Concept,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> Contrasts,
    ProofResponse Proof);

public sealed record ProofResponse(
    double HoldThreshold,
    IReadOnlyList<EvidenceClauseResponse> Accepted,

    /// The most a language model's opinion could ever contribute. Always below the threshold —
    /// and shown to the learner, because the honesty is the product.
    double ModelGradedCeiling);

public sealed record EvidenceClauseResponse(
    EvidenceType Type,
    double MinScore,
    double MaxContribution,
    int RequiredCount,

    /// Whether a language model decides this one. `false` means it cannot be talked into
    /// existence — a never-seen problem, code that ran, or a prediction reality settled.
    bool ModelGraded);

public static class ConceptMappings
{
    public static ConceptResponse ToResponse(this Concept c)
    {
        ArgumentNullException.ThrowIfNull(c);

        return new ConceptResponse(
            c.Id, c.Slug, c.Title, c.Summary, c.Domain, c.DifficultyPrior, c.EstimatedMinutes);
    }

    public static ProofResponse ToResponse(this PoMContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return new ProofResponse(
            contract.HoldThreshold,
            [.. contract.Accepted.Select(a => new EvidenceClauseResponse(
                a.Type, a.MinScore, a.MaxContribution, a.RequiredCount, a.Type.IsModelGraded()))],
            contract.ModelGradedCeiling);
    }
}
