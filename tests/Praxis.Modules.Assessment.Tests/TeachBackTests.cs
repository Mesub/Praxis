using Praxis.Modules.Assessment.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **Explain it out loud, to somebody who has never heard of it.**
///
/// The most revealing thing a learner can do, and the most dangerous thing to grade — because speech
/// carries a dozen signals that have **nothing whatever to do with knowing the thing**, and a naive
/// grader will happily score every one of them.
public sealed class TeachBackTests
{
    private static Transcript Heard(string text, double confidence = 0.95)
        => new(text, confidence);

    // ── A stammer is not a knowledge gap ─────────────────────────────────────

    /// **The most important test in this file.**
    ///
    /// These two learners have said **exactly the same thing**. One of them is nervous, or thinking,
    /// or has a stutter, or is speaking their third language.
    ///
    /// A grader that scores fluency is scoring **confidence, class, and first language** — and
    /// calling it understanding.
    [Fact]
    public void A_halting_explanation_and_a_fluent_one_are_graded_identically()
    {
        var halting = TeachBack.Prepare(Heard(
            "The, um, the inverted index, uh, maps terms to, to documents.")).Value;

        var fluent = TeachBack.Prepare(Heard(
            "The inverted index maps terms to documents.")).Value;

        Assert.Equal(fluent.Content, halting.Content);
    }

    /// **And here is the limit of the claim, stated honestly.**
    ///
    /// *So*, *right*, *well*, *basically* — they look like filler and they are not. *"So the tenth
    /// one counts less"* is a causal *so*; *"the right answer"* is a real word. A stripper that ate
    /// them would **damage the content**, and damaging the content is a far worse failure than
    /// leaving a marker in — because it would mark somebody down for something they actually said.
    ///
    /// So PRAXIS keeps them, and the judge sees a couple of discourse markers. That is a cost worth
    /// paying, and pretending otherwise would be the more dangerous lie.
    [Fact]
    public void A_word_that_might_be_content_is_kept_because_deleting_content_is_the_worse_failure()
    {
        var prepared = TeachBack.Prepare(Heard(
            "So the tenth occurrence counts less, so the right answer is saturation.")).Value;

        Assert.Contains("the tenth occurrence counts less", prepared.Content, StringComparison.Ordinal);
        Assert.Contains("the right answer is saturation", prepared.Content, StringComparison.Ordinal);
    }

    /// **Stripped, not weighted.** The judge is *physically unable* to react to the hesitation,
    /// because the hesitation is not in the text it is given.
    ///
    /// A grader told to "ignore disfluency" is a grader that has been asked nicely — and asking a
    /// language model nicely is the thing this entire codebase exists to stop relying on.
    [Fact]
    public void The_rubric_never_even_sees_the_hesitation()
    {
        var prepared = TeachBack.Prepare(Heard(
            "Um, so BM25, uh, saturates term frequency, you know, so the tenth one counts less."))
            .Value;

        foreach (var filler in new[] { "Um", "uh", "you know" })
        {
            Assert.DoesNotContain(filler, prepared.Content, StringComparison.OrdinalIgnoreCase);
        }

        // And the content survived intact.
        Assert.Contains("saturates term frequency", prepared.Content, StringComparison.Ordinal);
        Assert.Contains("tenth one counts less", prepared.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_word_is_a_stutter_and_not_an_emphasis()
    {
        var prepared = TeachBack.Prepare(Heard("The the index maps terms to documents.")).Value;

        Assert.Equal("The index maps terms to documents.", prepared.Content);
    }

    /// The disfluency count is recorded for the audit trail — and **nothing is allowed to act on
    /// it.** It is not a number about the learner.
    [Fact]
    public void How_much_someone_stumbled_is_recorded_but_never_acted_on()
    {
        var prepared = TeachBack.Prepare(Heard(
            "Um, uh, so, er, the index, um, maps terms.")).Value;

        Assert.True(prepared.DisfluenciesRemoved > 0);

        // It exists for the audit trail. It is not on the response, it is not in the score, and it
        // is not a fact about how much they know.
        Assert.DoesNotContain("um", prepared.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hedging is a calibration signal, not a penalty ───────────────────────

    /// **The instinct is exactly backwards.**
    ///
    /// *"I think it's maybe because term frequency saturates?"* sounds unsure, and every viva, every
    /// interview and every classroom in the world would mark it down.
    ///
    /// **Penalise hedging and you teach people to sound certain.** That is the worst lesson this
    /// system could teach. A learner who is unsure *and says so* is doing the single most valuable
    /// thing in the product (C-6): telling you their confidence honestly, so PRAXIS can find out
    /// whether they were **right** to be unsure.
    [Fact]
    public void Hedging_never_touches_the_content_the_rubric_grades()
    {
        var hedged = TeachBack.Prepare(Heard(
            "I think maybe BM25 saturates term frequency, possibly.")).Value;

        var certain = TeachBack.Prepare(Heard(
            "BM25 saturates term frequency.")).Value;

        // The hedges are *counted*...
        Assert.True(hedged.Hedges > 0);
        Assert.Equal(0, certain.Hedges);

        // ...and they are routed to calibration, where being unsure is *information*, not a fault.
        Assert.True(hedged.ExpressedConfidence < certain.ExpressedConfidence);
    }

    /// **The hedge is signal, so it is counted before anything is stripped.** If the disfluency
    /// remover ate the hedges, PRAXIS would lose the most honest thing the learner said.
    [Fact]
    public void The_hedges_are_counted_before_anything_is_removed()
    {
        var prepared = TeachBack.Prepare(Heard(
            "Um, I think, uh, maybe it saturates, I'm not sure.")).Value;

        Assert.True(
            prepared.Hedges >= 2,
            "The disfluency stripper ate the hedges. PRAXIS has just thrown away the most honest "
            + "thing the learner said, and it will now mark them as more confident than they are.");
    }

    /// A learner who says *"I'm not sure, but..."* and is then **right** is well calibrated and
    /// interesting. A learner who says it and is **wrong** knew they did not know — which is a
    /// completely healthy state. Neither of them should lose a mark for saying so.
    [Fact]
    public void Expressed_confidence_falls_with_hedging_and_never_below_a_floor()
    {
        var drowningInCaveats = TeachBack.Prepare(Heard(
            "I think maybe possibly perhaps it could be that I guess it might be saturation, "
            + "I'm not sure, I believe.")).Value;

        Assert.True(drowningInCaveats.ExpressedConfidence < 0.5);

        // But never zero. Somebody who hedged ten times still said something.
        Assert.True(drowningInCaveats.ExpressedConfidence >= 0.1);
    }

    // ── We do not grade a bad microphone ─────────────────────────────────────

    /// **What failed was our capture, not their understanding.**
    ///
    /// PRAXIS does not do its best with a mangled transcript and then write the result into a model
    /// of somebody's mind.
    [Fact]
    public void A_transcript_PRAXIS_could_not_hear_is_not_graded_at_all()
    {
        var refused = TeachBack.Prepare(Heard("the ... index ... mm ... terms", confidence: 0.31));

        Assert.False(refused.IsSuccess);

        Assert.Contains("could not hear that clearly", refused.Message!, StringComparison.Ordinal);
        Assert.Contains(
            "what failed was our recording, not your", refused.Message!, StringComparison.Ordinal);
    }

    /// And **nothing is recorded against them.** A learner is not marked down for our microphone.
    [Fact]
    public void A_failed_capture_costs_the_learner_nothing()
    {
        var refused = TeachBack.Prepare(Heard("...", confidence: 0.2));

        Assert.False(refused.IsSuccess);

        Assert.Contains(
            "Nothing has been recorded against you", refused.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_confidence_floor_is_high_enough_to_mean_something()
    {
        Assert.True(TeachBack.MinimumTranscriptionConfidence >= 0.70);
    }

    /// Silence is not an explanation — but it is not an accusation either.
    [Fact]
    public void An_empty_explanation_is_named_plainly_and_not_scored()
    {
        var refused = TeachBack.Prepare(Heard("um, uh, er..."));

        Assert.False(refused.IsSuccess);

        Assert.Contains("not a criticism", refused.Message!, StringComparison.Ordinal);
    }

    // ── And it still cannot confer mastery ───────────────────────────────────

    /// **The Evidence Ceiling, at the point where it is most tempting to forget it** (C-4).
    ///
    /// A learner has just given a genuinely brilliant explanation. Everything in the product wants to
    /// reward it. It **cannot make them held** — because a language model graded it, and the whole
    /// architecture rests on a model's opinion never being sufficient.
    [Fact]
    public void A_perfect_explanation_still_cannot_make_somebody_held()
    {
        var contract = new MasteryContract(
            HoldThreshold: 0.75,
            Clauses:
            [
                new EvidenceClause(EvidenceType.Explain, MinScore: 0.7, MaxContribution: 0.20, RequiredCount: 1),
                new EvidenceClause(EvidenceType.Apply, MinScore: 0.8, MaxContribution: 0.80, RequiredCount: 1),
            ]);

        var credit = TeachBackCredit.Credit(rubricScore: 1.0, contract).Value;

        Assert.True(credit > 0, "A brilliant explanation is worth *something*.");

        Assert.True(
            credit < contract.HoldThreshold,
            $"A perfect explanation just conferred {credit:F2} against a hold threshold of "
            + $"{contract.HoldThreshold:F2}. A language model has manufactured mastery, and every "
            + "claim in the Ledger is now worthless.");

        Assert.Equal(0.20, credit, tolerance: 1e-9);
    }

    [Fact]
    public void An_explanation_below_the_bar_is_worth_nothing()
    {
        var contract = new MasteryContract(
            0.75,
            [
                new EvidenceClause(EvidenceType.Explain, 0.7, 0.20, 1),
                new EvidenceClause(EvidenceType.Apply, 0.8, 0.80, 1),
            ]);

        Assert.Equal(0.0, TeachBackCredit.Credit(0.4, contract).Value);
    }

    /// **Some things cannot be talked into existence**, and the person who wrote the contract said
    /// so.
    [Fact]
    public void A_concept_that_does_not_accept_talk_does_not_accept_talk()
    {
        var buildOnly = new MasteryContract(
            0.75,
            [new EvidenceClause(EvidenceType.Build, 1.0, 1.0, 1)]);

        var refused = TeachBackCredit.Credit(1.0, buildOnly);

        Assert.False(refused.IsSuccess);

        Assert.Contains(
            "cannot be talked about into existence", refused.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_is_ceiling_bound_and_stays_that_way()
    {
        Assert.True(EvidenceType.Explain.IsCeilingBound());

        Assert.DoesNotContain(EvidenceType.Explain, EvidenceTypes.CanCrossTheHoldThreshold);
    }
}
