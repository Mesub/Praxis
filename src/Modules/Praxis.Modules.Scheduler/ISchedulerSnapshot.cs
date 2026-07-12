using Praxis.Modules.Scheduler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Scheduler;

/// How the Scheduler asks *"what is this person's situation, right now?"* — **without knowing the
/// Twin or the Compiler exist.**
///
/// Same seam as `ICompilerSnapshot`, for the same reason: the chooser is a pure function of plain
/// values, so it can be tested exhaustively without a database and reasoned about without holding
/// three engines in your head at once. The adapter lives at the composition root.
public interface ISchedulerSnapshot
{
    /// The learner's due reps and their next plan step, as of this instant.
    ///
    /// A `Result` rather than a bare value, because there is a real refusal underneath: a learner
    /// with no objective has no next step, and PRAXIS would rather say so than hand them a card
    /// pointing nowhere.
    Task<Result<SchedulerInputs>> ForLearnerAsync(
        Guid learnerId,
        int sessionMinutes,
        CancellationToken ct = default);
}
