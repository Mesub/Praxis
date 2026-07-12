namespace Praxis.Modules.Kernel.Projections;

/// How far a projection has read. The only mutable row the kernel owns — and it holds no
/// knowledge, only a bookmark.
public sealed class ProjectionCheckpoint
{
    public string ProjectionName { get; private set; } = null!;

    /// The sequence of the last event applied. <see cref="IEventStore.EmptyStream"/> means
    /// "has seen nothing", which is exactly the state a rebuild resets it to.
    public long LastSequence { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    private ProjectionCheckpoint() { }   // EF

    public static ProjectionCheckpoint StartOf(string projectionName, DateTime now) => new()
    {
        ProjectionName = projectionName,
        LastSequence = IEventStore.EmptyStream,
        UpdatedAt = now,
    };

    public void AdvanceTo(long sequence, DateTime now)
    {
        if (sequence < LastSequence)
        {
            throw new InvalidOperationException(
                $"Projection '{ProjectionName}' tried to move its checkpoint backwards, from "
                + $"{LastSequence} to {sequence}. A projection reads history forwards. Going "
                + "backwards would re-apply events it has already counted.");
        }

        LastSequence = sequence;
        UpdatedAt = now;
    }

    public void Rewind(DateTime now)
    {
        LastSequence = IEventStore.EmptyStream;
        UpdatedAt = now;
    }
}
