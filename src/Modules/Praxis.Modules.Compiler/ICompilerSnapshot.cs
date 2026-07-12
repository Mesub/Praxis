using Praxis.Modules.Compiler.Domain;

namespace Praxis.Modules.Compiler;

/// How the Compiler asks *"what does this graph look like, for this person, right now?"* — **without
/// knowing the Graph or the Twin exist.**
///
/// The Graph knows the shape of the subject. The Twin knows the shape of the learner. The Compiler
/// knows neither: it is handed one flat list of values and returns a plan. That is what lets it be
/// a solver rather than a service, tested exhaustively without a database, and reasoned about
/// without holding three engines in your head at once.
///
/// The adapter lives at the composition root, where the application is assembled.
public interface ICompilerSnapshot
{
    /// Everything reachable from the objective, with the Twin's live beliefs folded in — including
    /// how far each concept has *rotted*, which is the whole reason the plan is a function of the
    /// person and not of the syllabus.
    Task<IReadOnlyList<CompilerConcept>> ForObjectiveAsync(
        Guid learnerId,
        Guid objectiveConceptId,
        CancellationToken ct = default);
}
