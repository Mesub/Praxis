using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **One piece of proof, recorded forever — immutable, and honest about how far it can be trusted.**
public sealed class EvidenceTests
{
    private static readonly Guid Learner = Guid.CreateVersion7();
    private static readonly Guid Concept = Guid.CreateVersion7();
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Result<Evidence> Record(
        EvidenceType type, GradingMethod method, double score, double assurance)
        => Evidence.Record(Learner, Concept, type, method, score, assurance, sourceSequence: 42, When);

    // ── Honest assurance — the load-bearing refusal (C-6) ────────────────────

    /// **The Ledger will not tell a lie about certainty.** A perfect, eloquent EXPLAIN scores 1.0 —
    /// and PRAXIS still cannot be *sure* of it, because a language model graded it. Recording it at
    /// high assurance is refused.
    [Fact]
    public void A_model_graded_result_cannot_be_recorded_as_certain()
    {
        var overclaimed = Record(EvidenceType.Explain, GradingMethod.Rubric, score: 1.0, assurance: 0.99);

        Assert.False(overclaimed.IsSuccess);
        Assert.Contains("cannot be recorded at assurance", overclaimed.Message!, StringComparison.Ordinal);
        Assert.Contains("Record it honestly or not at all", overclaimed.Message!, StringComparison.Ordinal);
    }

    /// The same eloquent EXPLAIN, recorded at an **honest** assurance, is accepted. The refusal is
    /// about certainty, not about the evidence being worthless.
    [Fact]
    public void A_model_graded_result_at_an_honest_assurance_is_accepted()
    {
        var honest = Record(EvidenceType.Explain, GradingMethod.Rubric, score: 1.0,
            assurance: GradingMethods.RubricAssuranceCeiling);

        Assert.True(honest.IsSuccess);
        Assert.Equal(GradingMethods.RubricAssuranceCeiling, honest.Value.Assurance);
    }

    /// **Reality has no opinions.** A BUILD that ran, or a PREDICT the world resolved, can be recorded
    /// at full assurance — which is exactly why they are the types that can confer mastery.
    [Theory]
    [InlineData(GradingMethod.Execution)]
    [InlineData(GradingMethod.Reality)]
    [InlineData(GradingMethod.KeyMatch)]
    public void Evidence_graded_by_reality_or_a_key_can_be_fully_assured(GradingMethod method)
    {
        var type = method == GradingMethod.Execution ? EvidenceType.Build
            : method == GradingMethod.Reality ? EvidenceType.Predict
            : EvidenceType.Recall;

        var full = Record(type, method, score: 1.0, assurance: 1.0);

        Assert.True(full.IsSuccess);
        Assert.Equal(1.0, full.Value.Assurance);
    }

    /// **The ceiling is below 1.0, and meaningfully so** — the gap is PRAXIS admitting a model can be
    /// wrong. If this ever crept to 1.0, model-graded evidence would be indistinguishable from
    /// reality-graded, and the whole honesty guarantee would be gone.
    [Fact]
    public void The_rubric_ceiling_is_meaningfully_below_certainty()
    {
        Assert.True(GradingMethods.RubricAssuranceCeiling < 0.9);
        Assert.True(GradingMethod.Rubric.AssuranceCeiling() < GradingMethod.Execution.AssuranceCeiling());
    }

    // ── Assurance is a different axis from score ─────────────────────────────

    /// **A high score does not buy high assurance.** They are separate numbers, and conflating them is
    /// how a confident-sounding wrong answer would launder itself into a certainty.
    [Fact]
    public void Score_and_assurance_are_recorded_independently()
    {
        var e = Record(EvidenceType.Explain, GradingMethod.Rubric, score: 0.95, assurance: 0.60).Value;

        Assert.Equal(0.95, e.Score);
        Assert.Equal(0.60, e.Assurance);
    }

    // ── Immutable ────────────────────────────────────────────────────────────

    /// **Nothing about a recorded piece of evidence can be changed after the fact.** An append-only
    /// ledger whose rows can be edited is not append-only. Every property is `init`-only and there is
    /// no mutator — asserted by reflection, the same way the item bank asserts its absent answer column
    /// (M7.7a).
    [Fact]
    public void Recorded_evidence_is_immutable()
    {
        var publicSetters = typeof(Evidence).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Where(p => p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
                .All(m => m.Name != "IsExternalInit"))
            .ToList();

        Assert.Empty(publicSetters);

        foreach (var mutator in new[] { "SetScore", "Adjust", "Amend", "Correct", "Revise" })
        {
            Assert.DoesNotContain(mutator, typeof(Evidence).GetMethods().Select(m => m.Name));
        }
    }

    // ── Provenance and validation ────────────────────────────────────────────

    /// **The Ledger records nothing it cannot trace.** Every piece of evidence points back to the
    /// event-log position that produced it, so the whole chain is rebuildable from the log (the
    /// kernel's one rule).
    [Fact]
    public void Evidence_carries_the_provenance_of_the_event_that_produced_it()
    {
        var e = Evidence.Record(Learner, Concept, EvidenceType.Build, GradingMethod.Execution,
            1.0, 1.0, sourceSequence: 1337, When).Value;

        Assert.Equal(1337, e.SourceSequence);
        Assert.Equal(Learner, e.LearnerId);
        Assert.Equal(Concept, e.ConceptId);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_off_scale_score_is_refused(double score)
    {
        Assert.False(Record(EvidenceType.Build, GradingMethod.Execution, score, 1.0).IsSuccess);
    }

    [Fact]
    public void Evidence_must_belong_to_a_learner_and_a_concept()
    {
        Assert.Throws<ArgumentException>(() =>
            Evidence.Record(Guid.Empty, Concept, EvidenceType.Build, GradingMethod.Execution,
                1.0, 1.0, 1, When));
    }
}
