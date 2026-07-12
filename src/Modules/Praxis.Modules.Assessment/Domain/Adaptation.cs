using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **The selector hunts failure. This is the thing that knows when to stop.**
///
/// `ItemSelection` optimises for information, and a wrong answer is informative. Left to itself, it
/// will hand a struggling learner harder and harder items — **not out of malice, but out of
/// arithmetic.** Every wrong answer tells it something, so it keeps asking.
///
/// That is a machine correctly maximising the wrong thing at a human being, and this file exists to
/// take the decision away from it.
///
/// ## The three things it can say
///
/// **Escalate.** They are cruising. Two consecutive correct, well above their estimate, fast and
/// confident — so raise θ̂ *and change the evidence type*. This is the important half: getting harder
/// `RECALL` questions right proves nothing new, because **you cannot ceiling out of a `BUILD`**. When
/// somebody is beating the test, the answer is not a *harder* question. It is a **different kind of
/// question.**
///
/// **De-escalate.** They are drowning. Two consecutive wrong on things that should have been within
/// reach, and getting slower — which is what struggling actually looks like from the outside.
///
/// > **We do not keep hammering someone who is drowning.**
///
/// The session **stops assessing**. It does not lower the difficulty and carry on; it *stops*, hands
/// over to the tutor, and probes the prerequisites — because a learner failing at the level below
/// their estimate is not telling you about *this* concept. They are telling you something is broken
/// **underneath** it, and every further item you serve about this concept is a minute spent
/// measuring the wrong thing.
///
/// **Stop for tiredness.** Accuracy falling *and* latency rising, on things they were getting right
/// half an hour ago.
///
/// > **Tiredness is not ignorance.** Resume tomorrow. **No penalty.**
///
/// A test that cannot tell the difference between a learner who does not know something and a
/// learner who is exhausted will confidently record the second as the first — and then build a plan
/// around it, and decay it, and hold them to it. **The Twin would be wrong about a person because
/// they were tired on a Tuesday.**
public static class Adaptation
{
    /// Two in a row. One is luck; two is a pattern, and a pattern is the least you can act on
    /// without insulting somebody.
    public const int ConsecutiveSignal = 2;

    /// Correct at this far *above* their estimate means the estimate is wrong (too low).
    public const double CruisingMargin = 1.0;

    /// Wrong at this far *below* their estimate means something underneath is broken.
    public const double DrowningMargin = 0.5;

    public const double EscalationStep = 1.0;

    /// **Bigger than the escalation step, deliberately.** PRAXIS is quicker to admit it overestimated
    /// somebody than to congratulate itself for underestimating them — because the cost of the two
    /// errors is not symmetric. An overestimated learner is being handed work they cannot do, and is
    /// concluding that they are stupid.
    public const double DeEscalationStep = 1.5;

    public static Adjustment Consider(Ability ability, IReadOnlyList<Attempt> recent)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(recent);

        if (recent.Count < ConsecutiveSignal)
        {
            return Adjustment.None(ability);
        }

        var last = recent.TakeLast(ConsecutiveSignal).ToList();

        // ── Tiredness is not ignorance ───────────────────────────────────────
        //
        // Checked *first*, and deliberately so. A tired learner looks exactly like a struggling one —
        // and if PRAXIS mistakes the second for the first it will de-escalate them, tear up their
        // plan, and inject prerequisites they do not need, all because they had a long day.
        if (IsTiring(recent))
        {
            return new Adjustment(
                ability,
                AdaptationVerdict.StopForTiredness,
                "Your accuracy is falling and you are taking longer on things you were getting right "
                + "half an hour ago. That is tiredness, not ignorance — and PRAXIS is not going to "
                + "write down that you do not know something because you were tired on a Tuesday. "
                + "Come back tomorrow. **Nothing you have done here is lost, and there is no "
                + "penalty.**");
        }

        // ── Drowning ─────────────────────────────────────────────────────────
        if (last.All(a => !a.Correct && a.B <= ability.Theta - DrowningMargin)
            && IsSlowing(last))
        {
            return new Adjustment(
                new Ability(
                    Math.Max(Irt.MinTheta, ability.Theta - DeEscalationStep),
                    ability.StandardError),
                AdaptationVerdict.DeEscalate,
                "You are getting things wrong that should have been within reach, and you are "
                + "slowing down. PRAXIS is going to **stop testing you on this** — not because you "
                + "have failed it, but because failing here says something is missing *underneath* "
                + "it, and asking you the same question five more ways will not find out what. It "
                + "is going to go and look.");
        }

        // ── Cruising ─────────────────────────────────────────────────────────
        if (last.All(a => a.Correct && a.B >= ability.Theta + CruisingMargin && a.WasConfident))
        {
            return new Adjustment(
                new Ability(
                    Math.Min(Irt.MaxTheta, ability.Theta + EscalationStep),
                    ability.StandardError),
                AdaptationVerdict.Escalate,
                "You are beating this comfortably, so PRAXIS is not going to insult you with a "
                + "slightly harder version of the same question. **It is going to change the kind of "
                + "question** — because you cannot ceiling out of something you have to build.");
        }

        return Adjustment.None(ability);
    }

    /// **The escalation ladder is not "harder". It is "realer".**
    ///
    /// A learner who is acing `RECALL` has proved they can remember. Asking them a *harder* thing to
    /// remember measures the same faculty with a bigger number attached. The next honest question is
    /// a different *kind* of question — and at the end of the ladder is `BUILD`, which you cannot
    /// talk your way past, and `PREDICT`, which nobody can.
    public static EvidenceType Escalate(EvidenceType current) => current switch
    {
        EvidenceType.Recall => EvidenceType.Apply,
        EvidenceType.Apply => EvidenceType.Critique,
        EvidenceType.Critique => EvidenceType.Explain,
        EvidenceType.Explain => EvidenceType.Build,

        // **You cannot ceiling out of a BUILD.** There is nowhere harder to go, because the thing
        // either ran or it did not — and the only step beyond that is reality itself.
        EvidenceType.Build => EvidenceType.Predict,
        EvidenceType.Predict => EvidenceType.Predict,

        _ => throw new ArgumentOutOfRangeException(nameof(current), current, "Not an evidence type."),
    };

    /// Slowing down on the items they are getting wrong. This is what struggling looks like from
    /// outside a head.
    private static bool IsSlowing(List<Attempt> last)
        => last.Count >= 2 && last[^1].Seconds > last[0].Seconds;

    /// **Accuracy down *and* latency up**, across the session. Either one alone means nothing: a
    /// learner can be slow and right (thinking) or fast and wrong (guessing). It is the *pair* that
    /// says a person is running out.
    private static bool IsTiring(IReadOnlyList<Attempt> recent)
    {
        if (recent.Count < 6)
        {
            return false;   // not enough of a session to have a shape yet
        }

        var half = recent.Count / 2;

        var early = recent.Take(half).ToList();
        var late = recent.Skip(half).ToList();

        var accuracyFell = late.Count(a => a.Correct) / (double)late.Count
                         < early.Count(a => a.Correct) / (double)early.Count - 0.25;

        var slowedDown = late.Average(a => a.Seconds) > early.Average(a => a.Seconds) * 1.4;

        return accuracyFell && slowedDown;
    }
}

/// One thing the learner did, and what it cost them to do it.
public sealed record Attempt(
    bool Correct,

    /// The difficulty of the item they did it on.
    double B,

    /// **How long it took.** The single most under-used signal in assessment: it is the difference
    /// between *"I know this"* and *"I worked this out"*, and between *"I am thinking"* and *"I am
    /// finished"*.
    double Seconds,

    /// Did they say they were sure? Confidence stated **before** the outcome (C-6).
    bool WasConfident = false);

/// What PRAXIS decided to do about the learner in front of it.
public sealed record Adjustment(Ability Ability, AdaptationVerdict Verdict, string Says)
{
    public static Adjustment None(Ability ability)
        => new(ability, AdaptationVerdict.Continue, "Carry on.");

    /// **The session stops.** Either they are drowning or they are tired, and both are reasons to
    /// stop asking — not to ask more carefully.
    public bool ShouldStopAssessing
        => Verdict is AdaptationVerdict.DeEscalate or AdaptationVerdict.StopForTiredness;
}

public enum AdaptationVerdict
{
    Continue = 1,

    /// They are beating it. Raise the estimate — and **change the kind of question**, because a
    /// harder version of the same question proves nothing new.
    Escalate = 2,

    /// **They are drowning. Stop.** Hand to the tutor; probe the prerequisites. The failure is not
    /// about this concept — it is about something underneath it.
    DeEscalate = 3,

    /// **Tiredness is not ignorance.** Resume tomorrow. No penalty.
    StopForTiredness = 4,
}
