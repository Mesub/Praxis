namespace Praxis.Modules.Scheduler.Domain;

/// **One card. One action.**
///
/// *"What do I do in the next fifteen minutes?"*
///
/// Not a dashboard. Not a queue. Not a list of six things with a progress ring on each. The single
/// hardest moment in learning is **starting**, and a screen that presents a choice hands that moment
/// straight back to the person who is demonstrably bad at it.
///
/// So PRAXIS decides. It has the Twin, the Graph, the Compiler and the forgetting curve; the learner
/// has a spare fifteen minutes and a low tolerance for being asked to plan their own education. The
/// asymmetry is the entire product.
///
/// If the card is ever allowed to become a list, PRAXIS has lost.
public sealed record TodayCard(
    CardKind Kind,

    Guid ConceptId,
    string Slug,

    /// What the learner will actually be asked to do, in their own language. Not "Concept 14 of 30".
    string Headline,

    /// Why *this*, now. The learner is owed a reason — and a scheduler that cannot give one is a
    /// scheduler that is guessing.
    string Because,

    int Minutes,

    /// Everything else that was competing for this session, and lost. Shown nowhere by default; the
    /// learner can look if they want, and most never will. Its real job is to make the scheduler's
    /// decision *auditable* rather than magical.
    IReadOnlyList<Deferred> AlsoDue);

public enum CardKind
{
    /// Something they hold is slipping. This wins, and it wins for a reason (see `Scheduler`).
    Rescue = 1,

    /// A rep on schedule. Cheap, and it keeps the whole edifice standing.
    Rep = 2,

    /// The next concept in the plan. The frontier advancing — the one dopamine PRAXIS permits itself.
    Learn = 3,

    /// A concept they held and let rot far enough that it must be rebuilt, not merely refreshed.
    Refresh = 4,

    /// Nothing is due and the objective is reached. **A real state, and one PRAXIS says out loud
    /// rather than inventing work to fill.**
    Nothing = 5,
}

/// Something that was due and did not make the cut.
public sealed record Deferred(Guid ConceptId, string Slug, string Reason, int Minutes);
