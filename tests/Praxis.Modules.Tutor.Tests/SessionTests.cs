using System.Reflection;
using Praxis.Modules.Tutor.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Tests;

/// **Nobody leaves empty-handed.**
///
/// The commonest failure in education software does not look like failure while it is happening. A
/// learner opens a session, reads, nods along, takes four hints, feels like they understood every
/// word of it, closes the tab — and has produced **nothing**.
///
/// They will remember the *feeling* of comprehension and none of the content, and in three weeks
/// they will be baffled that they cannot do it, and they will conclude that they are bad at this.
/// **Fluency is not learning.** A session that ends in pure consumption has lied to somebody about
/// their own mind.
///
/// So PRAXIS will not record one.
public sealed class SessionTests
{
    private static Session Open(int minutes = 15)
        => Session.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), minutes);

    // ── The refusal ──────────────────────────────────────────────────────────

    /// **The rule the whole session exists to enforce.**
    [Fact]
    public void A_session_in_which_the_learner_made_nothing_cannot_be_completed()
    {
        var session = Open();

        // They read. They were helped. Four times.
        session.HintServed();
        session.LearnerSpoke();
        session.HintServed();
        session.LearnerSpoke();
        session.HintServed();
        session.LearnerSpoke();
        session.HintServed();

        Assert.False(session.MadeSomething);

        var refused = session.Complete(held: false, partial: false);

        Assert.False(refused.IsSuccess);

        Assert.Contains("has not produced anything", refused.Message!, StringComparison.Ordinal);
        Assert.Contains("mistake fluency for knowledge", refused.Message!, StringComparison.Ordinal);

        // And it is still open. PRAXIS did not quietly downgrade it and move on — the caller has to
        // do something honest.
        Assert.Equal(SessionState.Open, session.State);
        Assert.Null(session.Outcome);
    }

    /// The refusal is **specific**, and it tells the caller exactly what would fix it. A refusal that
    /// does not say what to do instead is just an obstacle.
    [Fact]
    public void The_refusal_names_the_three_things_that_would_satisfy_it()
    {
        var refused = Open().Complete(held: false, partial: false);

        var message = refused.Message!;

        Assert.Contains("an attempt", message, StringComparison.Ordinal);
        Assert.Contains("an explanation", message, StringComparison.Ordinal);
        Assert.Contains("where they got stuck", message, StringComparison.Ordinal);

        Assert.Contains("Nothing will not", message, StringComparison.Ordinal);
    }

    [Fact]
    public void One_thing_made_is_enough_to_close_a_session()
    {
        var session = Open();

        session.HintServed();
        session.Made();

        var completed = session.Complete(held: true, partial: false);

        Assert.True(completed.IsSuccess);
        Assert.Equal(SessionOutcome.Verified, completed.Value);
        Assert.Equal(SessionState.Completed, session.State);
    }

    // ── The stuck note. This is the whole product in miniature. ──────────────

    /// **A learner who ran out of budget and got stuck still made something.**
    ///
    /// They did not get the answer. They did not get mastery — the credit is **0.00**. What they
    /// produced is a *precise map of where their model broke*, which is worth more than the answer,
    /// and which PRAXIS could never have learned by watching them nod at a revealed one.
    ///
    /// The session ends `BlockedUnresolved`, and the Compiler recompiles their plan around it.
    [Fact]
    public void A_blocked_learner_who_writes_a_stuck_note_has_still_produced_something()
    {
        var session = Open();

        session.HintServed();
        session.LearnerSpoke();
        session.HintServed();

        // Out of budget. They write down exactly where their reasoning broke.
        session.WroteStuckNote();

        Assert.True(session.MadeSomething);

        var completed = session.Complete(held: false, partial: false);

        Assert.True(completed.IsSuccess);
        Assert.Equal(SessionOutcome.BlockedUnresolved, completed.Value);
    }

    /// **And a stuck note is not mastery.** It is misconception evidence (I-6) — it says where they
    /// broke, not that they are fixed. A session that granted credit for admitting confusion would
    /// be a very popular product and a completely worthless one.
    [Fact]
    public void A_stuck_note_can_never_produce_a_verified_session()
    {
        var session = Open();

        session.WroteStuckNote();

        // Even if the caller insists the concept is held, the outcome is BLOCKED — because nothing
        // was *produced*, and a note about being stuck is not a demonstration of anything.
        var completed = session.Complete(held: true, partial: true);

        Assert.Equal(SessionOutcome.BlockedUnresolved, completed.Value);

        Assert.NotEqual(SessionOutcome.Verified, completed.Value);
        Assert.NotEqual(SessionOutcome.Partial, completed.Value);
    }

    // ── The outcomes ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, SessionOutcome.Verified)]
    [InlineData(false, true, SessionOutcome.Partial)]
    [InlineData(false, false, SessionOutcome.Unverified)]
    public void What_they_produced_decides_what_the_session_was_worth(
        bool held, bool partial, SessionOutcome expected)
    {
        var session = Open();

        session.Made();

        Assert.Equal(expected, session.Complete(held, partial).Value);
    }

    /// **Four of the five outcomes grant nothing.** That is not pessimism — it is what honest
    /// measurement looks like when the thing being measured is genuinely hard.
    [Fact]
    public void Most_sessions_grant_no_mastery_and_that_is_the_honest_number()
    {
        var outcomes = Enum.GetValues<SessionOutcome>();

        Assert.Equal(5, outcomes.Length);

        // Exactly one grants full credit.
        Assert.Single(outcomes, o => o is SessionOutcome.Verified);
    }

    // ── Leaving is signal, not an error ──────────────────────────────────────

    /// **They walked away, and PRAXIS records it honestly** (I-10).
    ///
    /// No guilt notification. No streak to save. No "you were so close!" The Motivation Engine gets
    /// to ask *why* — because a learner who leaves is usually telling you something true about the
    /// session that a completion rate never will.
    [Fact]
    public void A_learner_who_walks_away_is_recorded_truthfully_and_not_chased()
    {
        var session = Open();

        session.HintServed();

        var left = session.Abandon();

        Assert.True(left.IsSuccess);
        Assert.Equal(SessionState.Abandoned, session.State);
        Assert.Equal(SessionOutcome.Abandoned, session.Outcome);
    }

    /// Abandonment is the honest alternative to the refusal — and it is *different*. It does not
    /// claim the session was completed; it claims they left, which is true.
    [Fact]
    public void Abandoning_is_allowed_even_when_nothing_was_produced()
    {
        var session = Open();

        Assert.False(session.MadeSomething);

        Assert.True(session.Abandon().IsSuccess);

        Assert.NotEqual(SessionOutcome.Verified, session.Outcome);
        Assert.Equal(SessionOutcome.Abandoned, session.Outcome);
    }

    [Fact]
    public void A_session_can_only_be_closed_once()
    {
        var session = Open();

        session.Made();
        session.Complete(held: true, partial: false);

        var again = session.Complete(held: true, partial: false);

        Assert.False(again.IsSuccess);
        Assert.Contains("already Completed", again.Message!, StringComparison.Ordinal);

        Assert.False(session.Abandon().IsSuccess);
    }

    // ── LR-3, at the session level ───────────────────────────────────────────

    /// **The tutor may not take two turns in a row.** Every hint ends with something for the learner
    /// to do, and nothing happens until they do it.
    [Fact]
    public void A_hint_always_leaves_the_ball_with_the_learner()
    {
        var session = Open();

        session.HintServed();

        Assert.True(session.AwaitingLearner);

        session.LearnerSpoke();

        Assert.False(session.AwaitingLearner);
    }

    [Fact]
    public void Producing_something_hands_the_turn_back()
    {
        var session = Open();

        session.HintServed();
        session.Made();

        Assert.False(session.AwaitingLearner);
    }

    // ── There is no back door ────────────────────────────────────────────────

    /// **The test that stops somebody unblocking a release.**
    ///
    /// There is no `ForceComplete`, no `MarkComplete`, no `SetOutcome`. If one appears, the refusal
    /// above becomes advisory — and an advisory refusal is a comment.
    [Fact]
    public void There_is_no_way_to_complete_a_session_that_bypasses_the_refusal()
    {
        var mutators = typeof(Session)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(Session))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] forbidden =
        [
            "ForceComplete", "MarkComplete", "SetOutcome", "SetState", "Finish", "MarkAsComplete",
        ];

        foreach (var name in forbidden)
        {
            Assert.False(
                mutators.Contains(name),
                $"Session.{name} exists. A session can now be closed without the learner having "
                + "produced anything, and the one rule that separates PRAXIS from a video player "
                + "is now advisory.");
        }
    }

    /// **`Complete` returns a `Result`, not `void`.**
    ///
    /// A `void Complete()` that threw would be swallowable by one `try/catch` written under
    /// deadline. A `Result` has to be *read*, and ignoring it is visible in the diff.
    [Fact]
    public void Completion_is_a_result_the_caller_has_to_look_at()
    {
        var complete = typeof(Session).GetMethod(
            "Complete", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Equal(typeof(Result<SessionOutcome>), complete.ReturnType);
    }

    [Fact]
    public void A_session_of_no_minutes_is_not_a_session()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Open(minutes: 0));
    }
}
