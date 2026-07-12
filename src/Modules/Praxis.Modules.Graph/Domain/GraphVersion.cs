using Praxis.Shared;

namespace Praxis.Modules.Graph.Domain;

/// A snapshot of what the graph claimed, at a moment.
///
/// The graph changes: prerequisites get added, difficulties get recalibrated, concepts get split.
/// But a piece of Evidence in the Ledger says "this learner demonstrated concept X" — and years
/// later, a stranger must be able to ask *what X meant when that was claimed*.
///
/// Without versioning, improving the graph silently rewrites the meaning of every credential ever
/// issued against it.
public sealed class GraphVersion : BaseEntity
{
    public string Domain { get; private set; } = null!;

    /// Monotonic per domain. Not semver — nobody negotiates compatibility with a knowledge graph.
    ///
    /// Named `Number` rather than `Version` because `BaseEntity.Version` is the row's concurrency
    /// token, and a property that shadows it would have EF quietly mapping the wrong one.
    public int Number { get; private set; }

    /// What changed and why. Read by a human, years later, trying to understand an old claim.
    public string Note { get; private set; } = null!;

    public int ConceptCount { get; private set; }
    public int EdgeCount { get; private set; }

    public DateTime PublishedAt { get; private set; }

    private GraphVersion() { }   // EF

    public static GraphVersion Publish(
        string domain,
        int number,
        string note,
        int conceptCount,
        int edgeCount,
        DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        return new GraphVersion
        {
            Domain = domain.Trim().ToLowerInvariant(),
            Number = number,
            Note = note.Trim(),
            ConceptCount = conceptCount,
            EdgeCount = edgeCount,
            PublishedAt = at,
        };
    }
}
