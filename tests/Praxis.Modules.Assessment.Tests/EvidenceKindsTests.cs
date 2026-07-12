using Praxis.Modules.Assessment.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **The six kinds of proof. They are not equal, and the difference is the product.**
public sealed class EvidenceKindsTests
{
    // ── The weights ──────────────────────────────────────────────────────────

    /// A set of weights that does not sum to one produces a mastery score that means **nothing** —
    /// and still looks exactly like a number.
    [Fact]
    public void The_six_weights_sum_to_exactly_one()
    {
        Assert.Equal(1.00, EvidenceKinds.TotalWeight, tolerance: 1e-9);
    }

    [Fact]
    public void Every_evidence_type_has_an_entry()
    {
        foreach (var type in Enum.GetValues<EvidenceType>())
        {
            var kind = EvidenceKinds.Of(type);

            Assert.Equal(type, kind.Type);
            Assert.True(kind.Weight > 0);
            Assert.True(kind.UpdateGain > 0);
        }

        Assert.Equal(6, EvidenceKinds.All.Count);
    }

    /// **RECALL is deliberately the lowest-weight evidence type**, and the reason is one sentence:
    ///
    /// > It is the only one of the six that a **search engine** can also do.
    ///
    /// A product that weights recall highly is not measuring a person. It is measuring their access
    /// to a lookup — and every learner who has ever crammed already knows the difference, even if
    /// the dashboard does not.
    [Fact]
    public void Recall_is_the_weakest_because_a_search_engine_can_do_it_too()
    {
        var lightest = EvidenceKinds.All.MinBy(k => k.Weight)!;

        Assert.Equal(EvidenceType.Recall, lightest.Type);

        Assert.True(
            EvidenceKinds.Of(EvidenceType.Recall).Weight
            < EvidenceKinds.Of(EvidenceType.Build).Weight,
            "Recall is now worth as much as building something that runs. PRAXIS is measuring "
            + "memory and calling it mastery.");
    }

    /// **BUILD and PREDICT carry the highest trust** — not because they are fashionable, but because
    /// they are the two types that are *structurally* hard to fake.
    [Fact]
    public void The_hardest_evidence_to_fake_moves_belief_the_most()
    {
        var strongest = EvidenceKinds.All.MaxBy(k => k.UpdateGain)!;

        Assert.Equal(EvidenceType.Build, strongest.Type);

        // One build that ran tells you a great deal, right now. Twenty correct recalls should still
        // leave you genuinely unsure — which is what a gain of 0.35 means.
        Assert.True(
            EvidenceKinds.Of(EvidenceType.Build).UpdateGain
            > 3 * EvidenceKinds.Of(EvidenceType.Recall).UpdateGain);
    }

    /// **Weight and gain are different questions.** BUILD is worth 0.25 of a concept and moves
    /// belief by 1.40; if the two were the same number, one of them would be redundant — and the
    /// one that got dropped would be the one nobody could explain.
    [Fact]
    public void Weight_and_update_gain_are_not_the_same_number()
    {
        var build = EvidenceKinds.Of(EvidenceType.Build);

        Assert.NotEqual(build.Weight, build.UpdateGain);

        Assert.True(build.Weight < 1.0);
        Assert.True(build.UpdateGain > 1.0);
    }

    // ── The Ceiling, and the trap that was hiding next to it ─────────────────

    /// **C-4.** Only these three can push a concept into HELD.
    [Fact]
    public void Only_the_unfakeable_three_can_confer_mastery()
    {
        var canConfer = EvidenceKinds.All
            .Where(k => k.CanConferMastery)
            .Select(k => k.Type)
            .ToHashSet();

        Assert.Equal(
            new HashSet<EvidenceType> { EvidenceType.Apply, EvidenceType.Build, EvidenceType.Predict },
            canConfer);
    }

    /// **The bug that was waiting to be "fixed".**
    ///
    /// This helper used to be called `IsModelGraded`, and the *behaviour* was right — but the name
    /// was a lie. `RECALL` is not model-graded. It is marked by an **exact key match**, with no
    /// model involved anywhere.
    ///
    /// So sooner or later somebody would read `IsModelGraded(Recall) == true`, check how recall is
    /// actually graded, find a string comparison, and conclude — **correctly, on the evidence in
    /// front of them** — that the code was wrong. They would change it to `false`. And in that
    /// instant RECALL becomes ceiling-free, and a learner can reach mastery by remembering things.
    ///
    /// **A well-meaning refactor would have gutted C-4, and every test would still have passed**,
    /// because the tests asserted the same lie.
    ///
    /// The two properties are orthogonal, and this test exists to keep them that way.
    [Fact]
    public void Recall_is_graded_deterministically_and_still_cannot_confer_mastery()
    {
        var recall = EvidenceKinds.Of(EvidenceType.Recall);

        // No model marks a recall item. It is a key match.
        Assert.Equal(GradingPath.KeyMatch, recall.Grading);

        // And it *still* cannot confer mastery — because it is fakeable by **looking it up**, not by
        // fooling a grader. Those are different attacks and they need different defences.
        Assert.True(recall.IsCeilingBound);
        Assert.False(recall.CanConferMastery);
    }

    /// The other half of the same point, from the other side: **being graded by a model is not the
    /// only reason to be ceiling-bound, and being graded deterministically is not a licence.**
    [Fact]
    public void The_ceiling_is_not_derivable_from_the_grading_path()
    {
        var deterministic = EvidenceKinds.All
            .Where(k => k.Grading is GradingPath.KeyMatch)
            .Select(k => k.Type)
            .ToHashSet();

        // Both RECALL and APPLY are key-matched...
        Assert.Contains(EvidenceType.Recall, deterministic);
        Assert.Contains(EvidenceType.Apply, deterministic);

        // ...and exactly one of them can confer mastery. If the ceiling were merely "was a model
        // involved?", these two would have the same answer. They do not, and they must not.
        Assert.False(EvidenceKinds.Of(EvidenceType.Recall).CanConferMastery);
        Assert.True(EvidenceKinds.Of(EvidenceType.Apply).CanConferMastery);
    }

    // ── Grading paths ────────────────────────────────────────────────────────

    /// > *Where truth is deterministic, we use determinism.* An LLM judge is a tool for grading
    /// > **arguments**, not for grading **arithmetic**.
    ///
    /// A language model is involved in exactly two of the six.
    [Fact]
    public void A_language_model_grades_only_two_of_the_six()
    {
        var modelGraded = EvidenceKinds.All
            .Where(k => k.Grading is GradingPath.Rubric)
            .Select(k => k.Type)
            .ToHashSet();

        Assert.Equal(
            new HashSet<EvidenceType> { EvidenceType.Explain, EvidenceType.Critique },
            modelGraded);
    }

    /// **A rubric can never override a failing test.** BUILD is settled by execution, not opinion.
    [Fact]
    public void A_build_is_settled_by_running_it()
    {
        Assert.Equal(GradingPath.Execution, EvidenceKinds.Of(EvidenceType.Build).Grading);
    }

    /// **PREDICT is the only evidence type in the world that cannot be cheated**, because the answer
    /// does not exist yet. There is no key to leak, no bank to share, no model to ask.
    ///
    /// *You cannot Google next Thursday.*
    [Fact]
    public void Reality_settles_a_prediction_and_nobody_can_argue_with_it()
    {
        var predict = EvidenceKinds.Of(EvidenceType.Predict);

        Assert.Equal(GradingPath.Reality, predict.Grading);
        Assert.True(predict.CanConferMastery);

        Assert.Contains("cannot be", predict.GamedBy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist yet", predict.StoppedBy, StringComparison.Ordinal);
    }

    // ── Every type is honest about how it gets cheated ───────────────────────

    /// **Each type says out loud how a learner would game it, and what actually stops them.**
    ///
    /// A threat model you have not written down is a threat model you do not have — and the most
    /// interesting entry in the whole table is RECALL's, whose defence is not a clever countermeasure
    /// at all. It is *"we simply don't weight it much"*. **Design-level immunity**: there is nothing
    /// to gain by cheating at the thing that is worth the least.
    [Fact]
    public void Every_type_states_how_it_is_gamed_and_what_stops_it()
    {
        foreach (var kind in EvidenceKinds.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.GamedBy), $"{kind.Type} has no threat model.");
            Assert.False(string.IsNullOrWhiteSpace(kind.StoppedBy), $"{kind.Type} has no defence.");
            Assert.False(string.IsNullOrWhiteSpace(kind.Proves));
        }

        // The one that is a *design* decision rather than a countermeasure.
        Assert.Contains(
            "not weighting it", EvidenceKinds.Of(EvidenceType.Recall).StoppedBy,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_evidence_type_with_no_entry_is_a_bug_and_says_so()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvidenceKinds.Of((EvidenceType)99));
    }

    /// Each type has its own intake — what the learner actually hands over. Six types, six intakes.
    [Fact]
    public void Each_type_has_its_own_intake()
    {
        var intakes = EvidenceKinds.All.Select(k => k.Intake).ToHashSet();

        Assert.Equal(6, intakes.Count);
    }
}
