using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Compiler.Domain;

/// **When is a plan out of date?**
///
/// A plan is a photograph of a learner at a moment. The moment it is taken, the learner starts
/// moving again — and every event in the log is a small argument that the photograph is now a lie.
///
/// Getting this wrong is expensive in both directions:
///
/// - **Recompile too rarely** and you hand someone a plan built on a person they no longer are. They
///   are told to study something they demonstrated last Tuesday, or sent at a concept whose
///   foundations have rotted since the plan was drawn. They lose faith in it, and they are right to.
///
/// - **Recompile too eagerly** and you burn the machine recomputing an identical answer, and — worse
///   — the plan *moves under the learner's feet*. A roadmap that reshuffles every time they blink is
///   not a roadmap; it is an anxiety generator. **Nothing is more corrosive to a plan than watching
///   it change for no reason you can see.**
///
/// So: **only events that could change the answer trigger a recompile.** Everything else waits for
/// the nightly pass, where a plan is refreshed against a day of decay whether or not anything
/// happened — because *time itself* changes the answer, and that is the one thing no event will ever
/// tell you.
public static class RecompileTrigger
{
    /// Does this event make the plan stale *right now*, rather than merely at the next nightly pass?
    public static bool RequiresImmediateRecompile(LearningEventType type) => type switch
    {
        // ── Yes: the shape of what they hold has changed ─────────────────────
        //
        // Accepted evidence is the only thing that moves mastery. A concept crossing into HELD
        // removes it from the plan entirely, and can unlock a branch that was blocked — which is a
        // different plan, and the learner should see it immediately. This is also the good news, and
        // good news should not have to wait until 3am.
        LearningEventType.EvidenceAccepted => true,

        // A named wrong belief suppresses the concept it sits under (M3.6), which can push a concept
        // *out* of held and put it back in the plan. That is precisely the case where continuing to
        // show the old plan sends someone at a wall.
        LearningEventType.MisconceptionDetected => true,

        // Reality settled a prediction. It is the least fakeable evidence there is, and it moves
        // belief; the plan should reflect that the moment it lands.
        LearningEventType.PredictionResolved => true,

        // ── No: it changes nothing the Compiler reads ────────────────────────
        //
        // Being taught something does not change what you can do. The Twin does not even listen to
        // this event (M3.7a), so recompiling on it would be recomputing an identical plan — and
        // shuffling the learner's roadmap for a reason they could not possibly perceive.
        LearningEventType.ConceptTaught => false,

        // An answer moves belief but not mastery credit, and the plan is ordered by *credit*. A
        // learner who gets three questions right has not finished anything, and their plan should
        // not visibly rearrange itself as though they had.
        LearningEventType.AnswerGiven => false,

        // Spending a hint costs them from the Answer Budget. It says something about the session; it
        // says nothing about what they hold.
        LearningEventType.HintTaken => false,

        // Talking. The Evidence Ceiling means it can never finish a concept, so it can never change
        // the plan's shape. Recompiling here would be the machine performing attentiveness.
        LearningEventType.ExplanationRecorded => false,

        // A rep keeps a concept alive — which is the plan working as intended, not the plan being
        // wrong. Reps land constantly; recompiling on each would mean recompiling all day.
        LearningEventType.RepAnswered => false,
        LearningEventType.RepDue => false,

        // Session lifecycle. Says nothing about the learner's knowledge.
        LearningEventType.SessionStarted => false,
        LearningEventType.SessionCompleted => false,

        // **Not even this one.** A learner who walked away needs the Motivation Engine (E13), not a
        // reshuffled roadmap. Rewriting their plan the moment they quit a session would be the
        // product flinching — and the next thing they see should be an unchanged plan and a smaller
        // next step, not evidence that PRAXIS panicked.
        LearningEventType.SessionAbandoned => false,

        // Rejected and abstained evidence: nothing was proved, so nothing has changed. Note this is
        // deliberately asymmetric with EvidenceAccepted — failing to prove something is the default
        // state, and the plan already assumes it.
        LearningEventType.EvidenceRejected => false,
        LearningEventType.EvidenceAbstained => false,

        // Predictions and artefacts, until they are adjudicated. The claim is not the proof.
        LearningEventType.PredictionMade => false,
        LearningEventType.ArtifactSubmitted => false,

        _ => false,
    };

    /// **Time alone makes a plan stale**, and no event will ever tell you so.
    ///
    /// Every day that passes, everything the learner holds decays a little. A concept that was fresh
    /// enough to build on yesterday may not be today — which changes the cost of everything above it
    /// and can put a "finished" concept back in the plan.
    ///
    /// This is the reason the nightly pass exists at all, and the reason it cannot be replaced by
    /// event-driven recompilation however clever the trigger list gets: **the most important thing
    /// that happens to a learner overnight is nothing.**
    public static bool IsStaleByAge(DateTime? lastCompiledAt, DateTime now)
        => lastCompiledAt is null || (now - lastCompiledAt.Value) >= MaxPlanAge;

    /// A plan older than this is recompiled whether or not anything happened.
    public static readonly TimeSpan MaxPlanAge = TimeSpan.FromHours(20);
}
