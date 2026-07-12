using Praxis.Modules.Graph.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// **The Evidence Ceiling (CANON C-4), enforced as arithmetic.**
///
/// The claim PRAXIS makes to the world is that a Ledger entry means something — that when it says
/// a person demonstrated a concept, they *did something* a machine could not do for them.
///
/// That claim is only as good as this file. If a contract can be authored in which model-graded
/// evidence reaches the hold threshold, then a hallucinating grader manufactures mastery, the Twin
/// becomes fiction, and every credential PRAXIS has ever issued is worthless.
///
/// So it is not a prompt, not a policy, not a runtime check on scores. It is refused at
/// construction, in arithmetic: *if every rubric in the world returned a perfect score, forever,
/// the learner would still fall short.*
public sealed class PoMContractTests
{
    private static readonly Guid Concept = Guid.CreateVersion7();

    [Fact]
    public void No_amount_of_talking_to_a_model_can_ever_produce_mastery()
    {
        // EXPLAIN and CRITIQUE and RECALL, all at their maximum, summing past the threshold.
        // This is the contract an author writes when they are trying to be generous, and it is
        // exactly the contract that would destroy the product.
        var ex = Assert.Throws<ArgumentException>(() => PoMContract.Create(
            Concept,
            holdThreshold: 0.80,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Explain, minScore: 0.7, maxContribution: 0.50),
                AcceptedEvidence.Of(EvidenceType.Critique, minScore: 0.7, maxContribution: 0.30),

                // Enough real work to satisfy the Floor, so it is the Ceiling that trips here.
                AcceptedEvidence.Of(EvidenceType.Apply, minScore: 0.8, maxContribution: 0.85),
            ]));

        Assert.Contains("hallucinating grader would manufacture mastery", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C-4", ex.Message, StringComparison.Ordinal);
    }

    /// **The Floor — the Ceiling's other half.**
    ///
    /// Unfakeable evidence must be *sufficient on its own*. If it is not, a learner who did every
    /// piece of real work the concept asks for would still need a rubric's blessing to be recorded
    /// as holding it — and the Ceiling's promise, that real work is what crosses the line, quietly
    /// inverts into its opposite.
    ///
    /// Real work is always enough. Talk is never enough. Talk only gets you there faster.
    [Fact]
    public void Real_work_alone_must_be_enough_to_hold_a_concept()
    {
        var ex = Assert.Throws<ArgumentException>(() => PoMContract.Create(
            Concept,
            holdThreshold: 0.80,
            accepted:
            [
                // Do every problem perfectly and you reach 0.70 — still short of 0.80. The only
                // way over the line would be an explanation a model liked.
                AcceptedEvidence.Of(EvidenceType.Apply, minScore: 0.8, maxContribution: 0.70),
                AcceptedEvidence.Of(EvidenceType.Explain, minScore: 0.7, maxContribution: 0.25),
            ]));

        Assert.Contains("Real work must be enough", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C-4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_where_real_work_exactly_reaches_the_threshold_is_accepted()
    {
        var contract = PoMContract.Create(
            Concept,
            holdThreshold: 0.80,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Apply, minScore: 0.8, maxContribution: 0.80),
                AcceptedEvidence.Of(EvidenceType.Explain, minScore: 0.7, maxContribution: 0.25),
            ]);

        // Real work is exactly enough; the explanation is an accelerant, not a requirement.
        Assert.Equal(0.80, contract.HoldThreshold);
        Assert.True(contract.ModelGradedCeiling < contract.HoldThreshold);
    }

    [Fact]
    public void Model_graded_evidence_alone_can_never_reach_the_threshold_even_at_the_limit()
    {
        // Exactly at the threshold is still a failure. "Reaches it" is not "stays below it".
        var ex = Assert.Throws<ArgumentException>(() => PoMContract.Create(
            Concept,
            holdThreshold: 0.70,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Explain, 0.7, maxContribution: 0.70),
                AcceptedEvidence.Of(EvidenceType.Build, 1.0, maxContribution: 0.75),
            ]));

        Assert.Contains("hallucinating grader", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C-4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_contract_offering_only_model_graded_evidence_is_unholdable_and_refused()
    {
        // Worse than unsafe: impossible. The learner could work forever and the ceiling would
        // hold them below the threshold for eternity. Better to refuse the contract than to ship
        // a concept nobody can ever finish.
        var ex = Assert.Throws<ArgumentException>(() => PoMContract.Create(
            Concept,
            holdThreshold: 0.80,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Explain, 0.7, 0.4),
                AcceptedEvidence.Of(EvidenceType.Recall, 0.9, 0.2),
            ]));

        Assert.Contains("could ever hold", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("APPLY, BUILD or PREDICT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_honest_contract_is_accepted()
    {
        // Talking about it gets you most of the way. It does not get you there.
        var contract = PoMContract.Create(
            Concept,
            holdThreshold: 0.80,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Recall, minScore: 0.9, maxContribution: 0.15),
                AcceptedEvidence.Of(EvidenceType.Explain, minScore: 0.7, maxContribution: 0.25),
                AcceptedEvidence.Of(EvidenceType.Apply, minScore: 0.8, maxContribution: 0.50, requiredCount: 2),
                AcceptedEvidence.Of(EvidenceType.Build, minScore: 1.0, maxContribution: 0.60),
            ]);

        Assert.Equal(0.80, contract.HoldThreshold);
        Assert.Equal(4, contract.Accepted.Count);

        // The ceiling: everything a model could ever grant, and it is not enough.
        Assert.Equal(0.40, contract.ModelGradedCeiling, precision: 10);
        Assert.True(contract.ModelGradedCeiling < contract.HoldThreshold);
    }

    [Fact]
    public void The_ceiling_holds_no_matter_how_the_author_arranges_the_clauses()
    {
        // Property-ish: for any contract that Create accepts, model-graded evidence at full
        // contribution cannot reach the threshold. There is no arrangement of clauses that gets
        // around it, because that is what Create checks.
        var thresholds = new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        var explainWeights = new[] { 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        var accepted = 0;

        foreach (var threshold in thresholds)
        {
            foreach (var explain in explainWeights)
            {
                foreach (var recall in explainWeights)
                {
                    PoMContract contract;

                    try
                    {
                        contract = PoMContract.Create(
                            Concept,
                            threshold,
                            [
                                AcceptedEvidence.Of(EvidenceType.Explain, 0.7, explain),
                                AcceptedEvidence.Of(EvidenceType.Recall, 0.9, recall),
                                AcceptedEvidence.Of(EvidenceType.Build, 1.0, 1.0),
                            ]);
                    }
                    catch (ArgumentException)
                    {
                        continue;   // refused, which is the ceiling doing its job
                    }

                    accepted++;

                    Assert.True(
                        contract.ModelGradedCeiling < contract.HoldThreshold,
                        $"A contract was accepted where model-graded evidence sums to "
                        + $"{contract.ModelGradedCeiling} against a threshold of {contract.HoldThreshold}.");
                }
            }
        }

        Assert.True(accepted > 0, "Every contract was refused — the test proves nothing.");
    }

    [Fact]
    public void A_concept_that_accepts_no_evidence_is_not_a_concept()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => PoMContract.Create(Concept, 0.8, []));

        Assert.Contains("topic, not a concept", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_evidence_type_cannot_be_listed_twice()
    {
        var ex = Assert.Throws<ArgumentException>(() => PoMContract.Create(
            Concept,
            0.8,
            [
                AcceptedEvidence.Of(EvidenceType.Build, 1.0, 0.5),
                AcceptedEvidence.Of(EvidenceType.Build, 0.5, 0.4),
            ]));

        Assert.Contains("listed twice", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void The_hold_threshold_is_a_mastery_credit(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PoMContract.Create(
            Concept, threshold, [AcceptedEvidence.Of(EvidenceType.Build, 1.0, 0.5)]));
    }

    [Fact]
    public void The_three_kinds_of_evidence_a_model_cannot_fake_are_named_and_only_three()
    {
        // If a fourth type were ever added to this set, the ceiling would leak. It is the load-
        // bearing list in the entire product.
        Assert.Equal(3, EvidenceTypes.CanCrossTheHoldThreshold.Count);
        Assert.Contains(EvidenceType.Apply, EvidenceTypes.CanCrossTheHoldThreshold);
        Assert.Contains(EvidenceType.Build, EvidenceTypes.CanCrossTheHoldThreshold);
        Assert.Contains(EvidenceType.Predict, EvidenceTypes.CanCrossTheHoldThreshold);

        Assert.True(EvidenceType.Explain.IsModelGraded());
        Assert.True(EvidenceType.Recall.IsModelGraded());
        Assert.True(EvidenceType.Critique.IsModelGraded());
        Assert.False(EvidenceType.Build.IsModelGraded());
    }
}
