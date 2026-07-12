using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Domain;

/// **One session, and the rule it exists to enforce: nobody leaves empty-handed.**
///
/// ## The refusal
///
/// **A session cannot be completed unless the learner made something.**
///
/// Not "should not". Cannot — `Complete()` returns a failure, and there is no other way to close a
/// session. This is the rule that separates PRAXIS from every product that has ever congratulated
/// somebody for watching a video.
///
/// The failure mode it forbids is the commonest one in education software, and it does not look like
/// failure while it is happening: a learner opens a session, reads, nods, takes four hints, feels
/// like they understood all of it, closes the tab, and has produced **nothing**. They will remember
/// the feeling of comprehension and none of the content, and in three weeks they will be baffled
/// that they cannot do it. *Fluency is not learning.* A session that ends in pure consumption is a
/// session that lied to somebody about their own mind, and PRAXIS will not record one.
///
/// ## And the part that makes it humane rather than punitive
///
/// A learner who runs out of budget and gets stuck **still made something**: the stuck note — *"where
/// exactly does your reasoning break?"* (M6.2). That counts. The session ends `BlockedUnresolved`,
/// mastery credit is **0.00**, and the Compiler recompiles their plan around whatever the note
/// revealed.
///
/// This is the whole product in miniature. The learner did not get the answer. They did not get
/// mastery. They got a **precise map of where their model broke** — which is worth more than the
/// answer, and which PRAXIS could not have learned from watching them nod at a revealed one.
public sealed class Session : BaseEntity
{
    public Guid LearnerId { get; private set; }
    public Guid ConceptId { get; private set; }

    public int PlannedMinutes { get; private set; }

    public SessionState State { get; private set; } = SessionState.Open;

    public SessionOutcome? Outcome { get; private set; }

    // ── What actually happened ───────────────────────────────────────────────

    /// Things the learner **made**: explanations, attempts, builds, a caught error, a real wrong
    /// answer that was genuinely reasoned (M6.2b).
    public int Produced { get; private set; }

    /// *"Where exactly does your reasoning break?"* Misconception evidence, never mastery evidence.
    public int StuckNotes { get; private set; }

    public int HintsTaken { get; private set; }

    /// **LR-3.** True when the tutor has spoken and the learner has not yet answered. The tutor may
    /// not take two turns in a row.
    public bool AwaitingLearner { get; private set; }

    /// **The test the whole session has to pass.** Something was said, written, drawn, built, or —
    /// at the very least — an honest account of where they got stuck.
    public bool MadeSomething => Produced > 0 || StuckNotes > 0;

    private Session() { }   // EF

    public static Session Start(Guid sessionId, Guid learnerId, Guid conceptId, int plannedMinutes)
    {
        if (plannedMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedMinutes), plannedMinutes, "A session of no minutes is not a session.");
        }

        return new Session
        {
            Id = sessionId,
            LearnerId = learnerId,
            ConceptId = conceptId,
            PlannedMinutes = plannedMinutes,
        };
    }

    // ── The turns ────────────────────────────────────────────────────────────

    internal void TutorSpoke() => AwaitingLearner = true;

    internal void LearnerSpoke() => AwaitingLearner = false;

    internal void HintServed()
    {
        HintsTaken++;
        AwaitingLearner = true;   // every hint ends with something for them to do
    }

    internal void Made()
    {
        Produced++;
        AwaitingLearner = false;
    }

    internal void WroteStuckNote()
    {
        StuckNotes++;
        AwaitingLearner = false;
    }

    // ── Closing it ───────────────────────────────────────────────────────────

    /// **The refusal.** A session in which the learner produced nothing cannot be completed.
    ///
    /// Note what PRAXIS does *not* do here: it does not silently downgrade the session to abandoned
    /// and move on. It **refuses**, and the caller's only honest options are to ask the learner for
    /// one thing — an attempt, an explanation, a stuck note, anything — or to record that they
    /// walked away (`Abandon`), which is a different and truthful claim.
    internal Result<SessionOutcome> Complete(bool held, bool partial)
    {
        if (State is not SessionState.Open)
        {
            return Result<SessionOutcome>.Conflict($"This session is already {State}.");
        }

        if (!MadeSomething)
        {
            return Result<SessionOutcome>.Invalid(
                "This session cannot be completed, because the learner has not produced anything. "
                + "They read, they were helped, and they made nothing — and a session that ends in "
                + "pure consumption teaches a person to mistake fluency for knowledge. Ask them for "
                + "one thing: an attempt, an explanation, or an honest note about where they got "
                + "stuck. Any of those will do. Nothing will not.");
        }

        // A stuck note is production — it is how a blocked learner still leaves having made
        // something — but it is **not mastery evidence** (I-6). It grants 0.00, and the Compiler
        // recompiles the plan around what it revealed.
        var outcome = Produced == 0
            ? SessionOutcome.BlockedUnresolved
            : held
                ? SessionOutcome.Verified
                : partial
                    ? SessionOutcome.Partial
                    : SessionOutcome.Unverified;

        State = SessionState.Completed;
        Outcome = outcome;

        return Result<SessionOutcome>.Success(outcome);
    }

    /// **They walked away, and that is signal rather than an error** (I-10).
    ///
    /// PRAXIS does not chase them, does not send a guilt notification, and does not pretend the
    /// session went well. It records what actually happened, and the Motivation Engine gets to ask
    /// *why* — because a learner who leaves is usually telling you something true about the session
    /// that a completion rate never will.
    internal Result Abandon()
    {
        if (State is not SessionState.Open)
        {
            return Result.Conflict($"This session is already {State}.");
        }

        State = SessionState.Abandoned;
        Outcome = SessionOutcome.Abandoned;

        return Result.Success();
    }

    /// Replay support: a projection rebuild starts from zero (M1.6).
    internal void Reset()
    {
        State = SessionState.Open;
        Outcome = null;
        Produced = 0;
        StuckNotes = 0;
        HintsTaken = 0;
        AwaitingLearner = false;
    }
}

public enum SessionState
{
    Open = 1,
    Completed = 2,

    /// They left. Not a failure — a fact, and one worth knowing.
    Abandoned = 3,
}

/// What the session was worth (CANON §5, *"Session outcomes and what they grant"*).
///
/// **Four of the five grant nothing.** That is not pessimism; it is what honest measurement looks
/// like when the thing being measured is hard.
public enum SessionOutcome
{
    /// They proved it. Full mastery credit; the Ledger signs it; E7 schedules the rep.
    Verified = 1,

    /// Halfway. Half credit, flagged, and re-queued within 48 hours while it is still warm.
    Partial = 2,

    /// They produced, and it did not hold up. **0.00** credit, misconception evidence only, and the
    /// concept comes back wearing a different representation.
    Unverified = 3,

    /// **The budget ran out and they wrote a stuck note.** 0.00 credit — and the single most useful
    /// artefact in the whole product, because it says *where* the model broke rather than merely
    /// that it did. The Compiler recompiles the plan around it.
    BlockedUnresolved = 4,

    /// They left. 0.00, an abandonment event, and a question for the Motivation Engine — never a
    /// notification badge.
    Abandoned = 5,
}
