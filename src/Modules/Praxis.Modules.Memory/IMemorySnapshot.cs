using Praxis.Modules.Memory.Domain;

namespace Praxis.Modules.Memory;

/// How E7 asks *"what does this person still hold, and how faded is it?"* — **without knowing the
/// Twin exists.**
///
/// Same seam as `ICompilerSnapshot` and `ISchedulerSnapshot`, for the same reason: **no module reads
/// another module's tables.** The Twin knows what a learner has proved and when. E7 knows what
/// forgetting does to it. Neither needs the other's schema, and the adapter lives at the composition
/// root where the application is assembled.
///
/// It also keeps the rep queue a **pure function of plain values** — which is what let M8.2 sweep the
/// entire model in a unit test and find the sweet spot, rather than asserting a number somebody liked
/// the look of.
public interface IMemorySnapshot
{
    /// Everything this learner **holds**, with how long it has been since they last proved they still
    /// did.
    ///
    /// Only what they *hold*. A concept they never proved cannot be "due" — it is simply not learned
    /// yet, and that is the plan's business, not the review queue's. A queue that pads itself with
    /// things you were never taught is a queue optimising for looking busy.
    Task<IReadOnlyList<DueRep>> HeldByAsync(Guid learnerId, CancellationToken ct = default);
}
