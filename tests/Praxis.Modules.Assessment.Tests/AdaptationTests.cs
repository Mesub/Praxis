using Praxis.Modules.Assessment.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **The selector hunts failure. This is the thing that knows when to stop.**
///
/// An engine optimising for information will hand a struggling learner harder and harder items — not
/// out of malice, but out of **arithmetic**. Every wrong answer tells it something, so it keeps
/// asking. That is a machine correctly maximising the wrong thing at a human being.
public sealed class AdaptationTests
{
    private static Ability At(double theta, double se = 0.5) => new(theta, se);

    // ── We do not keep hammering someone who is drowning ─────────────────────

    /// **The rule this whole file exists for.**
    ///
    /// Two wrong, on things that should have been *within reach*, and getting slower. PRAXIS stops
    /// assessing. It does not lower the difficulty and carry on — it **stops**, and goes looking
    /// underneath.
    ///
    /// A learner failing *below* their own estimate is not telling you about this concept. They are
    /// telling you something is broken **underneath** it, and every further item you serve about this
    /// concept is a minute spent measuring the wrong thing.
    [Fact]
    public void A_learner_who_is_drowning_is_not_handed_another_item()
    {
        var adjustment = Adaptation.Consider(
            At(1.0),
            [
                new Attempt(Correct: false, B: 0.2, Seconds: 40),
                new Attempt(Correct: false, B: 0.1, Seconds: 95),   // and slowing down
            ]);

        Assert.Equal(AdaptationVerdict.DeEscalate, adjustment.Verdict);

        Assert.True(
            adjustment.ShouldStopAssessing,
            "PRAXIS is about to serve another item to somebody who is visibly drowning.");

        Assert.Contains("stop testing you on this", adjustment.Says, StringComparison.Ordinal);
        Assert.Contains("something is missing *underneath*", adjustment.Says, StringComparison.Ordinal);
    }

    /// **PRAXIS is quicker to admit it overestimated somebody than to congratulate itself for
    /// underestimating them.**
    ///
    /// The costs are not symmetric. An overestimated learner is being handed work they cannot do —
    /// and is quietly concluding that they are stupid.
    [Fact]
    public void Praxis_climbs_down_faster_than_it_climbs_up()
    {
        Assert.True(Adaptation.DeEscalationStep > Adaptation.EscalationStep);
    }

    [Fact]
    public void De_escalation_actually_lowers_the_estimate()
    {
        var adjustment = Adaptation.Consider(
            At(1.0),
            [
                new Attempt(false, B: 0.3, Seconds: 30),
                new Attempt(false, B: 0.2, Seconds: 80),
            ]);

        Assert.True(adjustment.Ability.Theta < 1.0);
        Assert.Equal(-0.5, adjustment.Ability.Theta, tolerance: 1e-9);   // 1.0 − 1.5
    }

    /// Getting hard things wrong is **not** drowning. It is the test working — the boundary is
    /// exactly where PRAXIS is trying to be.
    [Fact]
    public void Failing_something_genuinely_hard_is_not_drowning()
    {
        var adjustment = Adaptation.Consider(
            At(0.0),
            [
                new Attempt(false, B: 2.0, Seconds: 40),
                new Attempt(false, B: 2.2, Seconds: 60),
            ]);

        Assert.Equal(AdaptationVerdict.Continue, adjustment.Verdict);
        Assert.False(adjustment.ShouldStopAssessing);
    }

    // ── Tiredness is not ignorance ───────────────────────────────────────────

    /// **The most important sentence in this file.**
    ///
    /// A test that cannot tell the difference between a learner who does not know something and a
    /// learner who is *exhausted* will confidently record the second as the first — and then build a
    /// plan around it, and decay it, and hold them to it.
    ///
    /// **The Twin would be wrong about a person because they were tired on a Tuesday.**
    [Fact]
    public void A_tired_learner_is_sent_home_and_not_marked_down()
    {
        // Getting them right, quickly, half an hour ago...
        List<Attempt> session =
        [
            new(true, 0.0, 20), new(true, 0.0, 22), new(true, 0.0, 25),

            // ...and now slow, and wrong.
            new(false, 0.0, 65), new(false, 0.0, 70), new(false, 0.0, 80),
        ];

        var adjustment = Adaptation.Consider(At(0.5), session);

        Assert.Equal(AdaptationVerdict.StopForTiredness, adjustment.Verdict);
        Assert.True(adjustment.ShouldStopAssessing);

        Assert.Contains("tiredness, not ignorance", adjustment.Says, StringComparison.Ordinal);
        Assert.Contains("tired on a Tuesday", adjustment.Says, StringComparison.Ordinal);
        Assert.Contains("no penalty", adjustment.Says, StringComparison.OrdinalIgnoreCase);
    }

    /// **A tired learner's estimate is not lowered.** That is the entire point — PRAXIS refuses to
    /// write down that they do not know something on the strength of a long day.
    [Fact]
    public void Being_tired_does_not_cost_a_learner_their_estimate()
    {
        List<Attempt> session =
        [
            new(true, 0.0, 20), new(true, 0.0, 22), new(true, 0.0, 25),
            new(false, 0.0, 65), new(false, 0.0, 70), new(false, 0.0, 80),
        ];

        var adjustment = Adaptation.Consider(At(0.5), session);

        Assert.Equal(0.5, adjustment.Ability.Theta, tolerance: 1e-9);
    }

    /// **Tiredness is checked before drowning, and that ordering is load-bearing.**
    ///
    /// A tired learner looks exactly like a struggling one. If PRAXIS mistakes the second for the
    /// first it will de-escalate them, tear up their plan, and inject prerequisites they do not need
    /// — all because they had a long day.
    [Fact]
    public void A_tired_learner_is_not_mistaken_for_a_drowning_one()
    {
        List<Attempt> session =
        [
            new(true, 0.0, 20), new(true, 0.0, 22), new(true, 0.0, 24),
            new(false, 0.0, 60), new(false, 0.0, 70), new(false, 0.0, 90),
        ];

        // This *also* satisfies the drowning pattern — wrong, below θ, slowing. Tiredness wins.
        var adjustment = Adaptation.Consider(At(1.0), session);

        Assert.Equal(AdaptationVerdict.StopForTiredness, adjustment.Verdict);
        Assert.NotEqual(AdaptationVerdict.DeEscalate, adjustment.Verdict);
    }

    /// Slow **and right** is thinking, not tiring. Fast **and wrong** is guessing, not tiring. It is
    /// the *pair* — accuracy down *and* latency up — that says a person is running out.
    [Fact]
    public void Being_slow_and_right_is_thinking_not_tiring()
    {
        List<Attempt> session =
        [
            new(true, 0.0, 20), new(true, 0.0, 22), new(true, 0.0, 25),
            new(true, 0.0, 70), new(true, 0.0, 80), new(true, 0.0, 90),
        ];

        Assert.NotEqual(
            AdaptationVerdict.StopForTiredness,
            Adaptation.Consider(At(0.5), session).Verdict);
    }

    // ── Escalation: the ladder is not "harder", it is "realer" ───────────────

    [Fact]
    public void A_learner_who_is_cruising_gets_a_higher_estimate()
    {
        var adjustment = Adaptation.Consider(
            At(0.0),
            [
                new Attempt(true, B: 1.2, Seconds: 15, WasConfident: true),
                new Attempt(true, B: 1.5, Seconds: 12, WasConfident: true),
            ]);

        Assert.Equal(AdaptationVerdict.Escalate, adjustment.Verdict);
        Assert.Equal(1.0, adjustment.Ability.Theta, tolerance: 1e-9);
    }

    /// **When somebody is beating the test, the answer is not a harder question. It is a different
    /// kind of question.**
    ///
    /// A learner acing `RECALL` has proved they can remember. A *harder* thing to remember measures
    /// the same faculty with a bigger number attached.
    [Fact]
    public void Escalation_changes_the_kind_of_question_not_just_the_difficulty()
    {
        var adjustment = Adaptation.Consider(
            At(0.0),
            [
                new Attempt(true, 1.2, 15, WasConfident: true),
                new Attempt(true, 1.4, 14, WasConfident: true),
            ]);

        Assert.Contains("change the kind of", adjustment.Says, StringComparison.Ordinal);
        Assert.Contains("cannot ceiling out of", adjustment.Says, StringComparison.Ordinal);
    }

    /// **You cannot ceiling out of a BUILD.** The ladder ends where opinions do — the thing either
    /// ran or it did not, and the only step beyond that is reality itself.
    [Fact]
    public void The_ladder_climbs_towards_the_things_that_cannot_be_faked()
    {
        Assert.Equal(EvidenceType.Apply, Adaptation.Escalate(EvidenceType.Recall));
        Assert.Equal(EvidenceType.Critique, Adaptation.Escalate(EvidenceType.Apply));
        Assert.Equal(EvidenceType.Explain, Adaptation.Escalate(EvidenceType.Critique));
        Assert.Equal(EvidenceType.Build, Adaptation.Escalate(EvidenceType.Explain));

        // And past BUILD is the only thing harder than execution: the world.
        Assert.Equal(EvidenceType.Predict, Adaptation.Escalate(EvidenceType.Build));

        // There is nowhere above PREDICT to go. Nobody can cheat at next Thursday.
        Assert.Equal(EvidenceType.Predict, Adaptation.Escalate(EvidenceType.Predict));
    }

    /// **The ladder ends at evidence a model cannot fake.**
    [Fact]
    public void Escalating_far_enough_always_lands_on_unfakeable_evidence()
    {
        var type = EvidenceType.Recall;

        for (var i = 0; i < 10; i++)
        {
            type = Adaptation.Escalate(type);
        }

        Assert.Contains(type, EvidenceTypes.CanCrossTheHoldThreshold);
        Assert.Equal(EvidenceType.Predict, type);
    }

    /// Getting two right that they **were not sure about** is not cruising. It might be luck, and
    /// PRAXIS does not promote people on luck.
    [Fact]
    public void Two_lucky_guesses_are_not_a_promotion()
    {
        var adjustment = Adaptation.Consider(
            At(0.0),
            [
                new Attempt(true, 1.2, 15, WasConfident: false),
                new Attempt(true, 1.5, 14, WasConfident: false),
            ]);

        Assert.Equal(AdaptationVerdict.Continue, adjustment.Verdict);
    }

    // ── One is luck. Two is a pattern. ───────────────────────────────────────

    [Fact]
    public void One_bad_answer_is_not_a_pattern()
    {
        var adjustment = Adaptation.Consider(
            At(1.0), [new Attempt(false, 0.2, 90)]);

        Assert.Equal(AdaptationVerdict.Continue, adjustment.Verdict);
    }

    [Fact]
    public void A_signal_needs_two_in_a_row()
    {
        Assert.Equal(2, Adaptation.ConsecutiveSignal);
    }
}
