using Praxis.Modules.Twin.Learning;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Tests;

/// **If these tests are ever red, PRAXIS is a lie.**
///
/// The product's claim to the world is that a Ledger entry means the person *did something a
/// machine could not do for them*. That claim is worth exactly as much as this file.
public sealed class EvidenceCeilingTests
{
    /// A contract in the shape the real pack uses: talking gets you most of the way, and never all
    /// of it.
    private static readonly MasteryContract Contract = new(
        HoldThreshold: 0.80,
        Clauses:
        [
            new EvidenceClause(EvidenceType.Recall, MinScore: 0.9, MaxContribution: 0.15, RequiredCount: 1),
            new EvidenceClause(EvidenceType.Explain, MinScore: 0.7, MaxContribution: 0.25, RequiredCount: 1),
            new EvidenceClause(EvidenceType.Critique, MinScore: 0.7, MaxContribution: 0.20, RequiredCount: 1),
            new EvidenceClause(EvidenceType.Apply, MinScore: 0.8, MaxContribution: 0.60, RequiredCount: 2),
            new EvidenceClause(EvidenceType.Build, MinScore: 1.0, MaxContribution: 0.85, RequiredCount: 1),
        ]);

    /// **THE PROPERTY.**
    ///
    /// No sequence of model-graded evidence — of any length, at any score, with any assurance —
    /// can ever produce a held concept. Not ten explanations. Not ten thousand. Not perfect ones.
    ///
    /// This is what makes a hallucinating grader harmless: it can make PRAXIS *more optimistic*
    /// about a learner. It can never make PRAXIS *certify* them.
    [Fact]
    public void No_quantity_of_model_graded_evidence_can_ever_produce_a_held_concept()
    {
        var lengths = new[] { 1, 2, 5, 10, 100, 1_000, 10_000 };
        var scores = new[] { 0.7, 0.85, 0.95, 1.0 };
        var assurances = new[] { 0.5, 0.9, 1.0 };
        var modelGraded = new[] { EvidenceType.Recall, EvidenceType.Explain, EvidenceType.Critique };

        var cases = 0;

        foreach (var length in lengths)
        {
            foreach (var score in scores)
            {
                foreach (var assurance in assurances)
                {
                    // Every model-graded type, all at once, over and over, all perfect.
                    var evidence = Enumerable.Range(0, length)
                        .SelectMany(_ => modelGraded.Select(t => new AcceptedEvidenceRecord(t, score, assurance)))
                        .ToList();

                    var result = MasteryCredit.Compute(Contract, evidence);

                    cases++;

                    Assert.False(
                        result.IsHeld,
                        $"{length} × every model-graded evidence type, at score {score} and "
                        + $"assurance {assurance}, produced a HELD concept "
                        + $"(credit {result.MasteryCredit:F4} against a threshold of "
                        + $"{Contract.HoldThreshold}). A hallucinating grader has just manufactured "
                        + "mastery, and the Ledger is worthless.");

                    Assert.Equal(0.0, result.FromUnfakeableEvidence, precision: 9);
                }
            }
        }

        Assert.True(cases > 50, "The sweep did not actually run.");
    }

    /// The mirror property: **real work is always enough.** A learner who does everything the
    /// concept genuinely asks for holds it, without needing any model to approve of them.
    [Fact]
    public void Real_work_alone_is_always_enough()
    {
        var evidence = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Build, Score: 1.0, Assurance: 1.0),
        };

        var result = MasteryCredit.Compute(Contract, evidence);

        Assert.True(result.IsHeld, $"A build that ran left the learner at {result.MasteryCredit:F2}.");
        Assert.Equal(1.0, result.Assurance, precision: 6);
    }

    [Fact]
    public void Talking_gets_you_close_and_stops()
    {
        // The honest experience PRAXIS wants a learner to have: you can feel yourself approaching
        // it, and you can feel that the last step is not made of words.
        var talk = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Recall, 1.0, 1.0),
            new(EvidenceType.Explain, 1.0, 1.0),
            new(EvidenceType.Critique, 1.0, 1.0),
        };

        var result = MasteryCredit.Compute(Contract, talk);

        Assert.False(result.IsHeld);

        // Not nothing. It moved. It just did not finish.
        Assert.True(result.MasteryCredit > 0.5, $"Perfect explanations were worth only {result.MasteryCredit:F2}.");
        Assert.True(result.MasteryCredit < Contract.HoldThreshold);

        // And every bit of it is model-graded, which the Twin records honestly.
        Assert.Equal(0.0, result.Assurance, precision: 9);
    }

    [Fact]
    public void One_never_seen_problem_solved_finishes_what_the_talking_started()
    {
        var evidence = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Recall, 1.0, 1.0),
            new(EvidenceType.Explain, 1.0, 1.0),
            new(EvidenceType.Critique, 1.0, 1.0),
            new(EvidenceType.Apply, 1.0, 1.0),
            new(EvidenceType.Apply, 1.0, 1.0),
        };

        var result = MasteryCredit.Compute(Contract, evidence);

        Assert.True(result.IsHeld);
        Assert.True(result.FromUnfakeableEvidence > 0);
    }

    /// **The Twin does not trust the Graph to have done its job.**
    ///
    /// Even handed a corrupt contract — one where model-graded evidence *could* reach the threshold
    /// — the belief update refuses to let it. Two engines, two refusals, and neither depends on the
    /// other being correct.
    [Fact]
    public void A_corrupt_contract_still_cannot_manufacture_mastery()
    {
        // A contract the Graph would never construct: 1.10 of model-graded contribution against a
        // 0.80 threshold. If it somehow reached the Twin — a bad migration, a hand-written row, a
        // future bug — the ceiling still holds.
        var corrupt = new MasteryContract(
            HoldThreshold: 0.80,
            Clauses:
            [
                new EvidenceClause(EvidenceType.Explain, 0.7, MaxContribution: 0.60, RequiredCount: 1),
                new EvidenceClause(EvidenceType.Critique, 0.7, MaxContribution: 0.50, RequiredCount: 1),
                new EvidenceClause(EvidenceType.Build, 1.0, MaxContribution: 0.90, RequiredCount: 1),
            ]);

        Assert.True(corrupt.ModelGradedCeiling > corrupt.HoldThreshold, "The fixture is not corrupt.");

        var allTalk = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Explain, 1.0, 1.0),
            new(EvidenceType.Critique, 1.0, 1.0),
        };

        var result = MasteryCredit.Compute(corrupt, allTalk);

        Assert.False(
            result.IsHeld,
            "A corrupt contract carried a learner over the threshold on explanations alone. The "
            + "Twin trusted the Graph to have done its job, and it had not.");
    }

    // ── Caps ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ten_perfect_explanations_are_not_a_build_that_runs()
    {
        var tenExplanations = Enumerable.Range(0, 10)
            .Select(_ => new AcceptedEvidenceRecord(EvidenceType.Explain, 1.0, 1.0))
            .ToList();

        var oneBuild = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Build, 1.0, 1.0),
        };

        var talked = MasteryCredit.Compute(Contract, tenExplanations);
        var built = MasteryCredit.Compute(Contract, oneBuild);

        Assert.False(talked.IsHeld);
        Assert.True(built.IsHeld);

        // The tenth explanation is worth exactly as much as the second: nothing.
        var oneExplanation = MasteryCredit.Compute(
            Contract, [new AcceptedEvidenceRecord(EvidenceType.Explain, 1.0, 1.0)]);

        Assert.Equal(oneExplanation.MasteryCredit, talked.MasteryCredit, precision: 9);
    }

    [Fact]
    public void Producing_half_of_what_is_required_is_worth_half_the_clause()
    {
        // The contract asks for two APPLYs. One is not two.
        var one = MasteryCredit.Compute(Contract, [new AcceptedEvidenceRecord(EvidenceType.Apply, 1.0, 1.0)]);

        var two = MasteryCredit.Compute(Contract,
        [
            new AcceptedEvidenceRecord(EvidenceType.Apply, 1.0, 1.0),
            new AcceptedEvidenceRecord(EvidenceType.Apply, 1.0, 1.0),
        ]);

        Assert.Equal(two.MasteryCredit / 2, one.MasteryCredit, precision: 9);
    }

    [Fact]
    public void Evidence_below_the_standard_is_worth_nothing()
    {
        // The contract demands a BUILD at score 1.0 — it ran, or it did not. A build that half-ran
        // is a build that did not run.
        var result = MasteryCredit.Compute(
            Contract, [new AcceptedEvidenceRecord(EvidenceType.Build, Score: 0.9, Assurance: 1.0)]);

        Assert.Equal(0.0, result.MasteryCredit, precision: 9);
        Assert.False(result.IsHeld);
    }

    [Fact]
    public void Evidence_the_contract_does_not_accept_is_ignored()
    {
        var minimal = new MasteryContract(
            HoldThreshold: 0.80,
            Clauses: [new EvidenceClause(EvidenceType.Build, 1.0, 0.90, 1)]);

        var result = MasteryCredit.Compute(minimal,
        [
            new AcceptedEvidenceRecord(EvidenceType.Explain, 1.0, 1.0),
            new AcceptedEvidenceRecord(EvidenceType.Recall, 1.0, 1.0),
        ]);

        Assert.Equal(0.0, result.MasteryCredit, precision: 9);
    }

    // ── Assurance ────────────────────────────────────────────────────────────

    [Fact]
    public void Low_assurance_evidence_is_worth_less_and_the_twin_says_so()
    {
        // A learner whose calibration says they cannot predict themselves (M3.4) has their evidence
        // discounted. The belief still moves; it just moves less, and the Ledger records why.
        var trusted = MasteryCredit.Compute(
            Contract, [new AcceptedEvidenceRecord(EvidenceType.Build, 1.0, Assurance: 1.0)]);

        var doubted = MasteryCredit.Compute(
            Contract, [new AcceptedEvidenceRecord(EvidenceType.Build, 1.0, Assurance: 0.35)]);

        Assert.True(doubted.MasteryCredit < trusted.MasteryCredit);
        Assert.True(doubted.MasteryCredit > 0, "The evidence was destroyed rather than discounted.");
    }

    [Fact]
    public void Assurance_reports_how_much_of_the_belief_rests_on_real_work()
    {
        var mixed = MasteryCredit.Compute(Contract,
        [
            new AcceptedEvidenceRecord(EvidenceType.Explain, 1.0, 1.0),
            new AcceptedEvidenceRecord(EvidenceType.Build, 1.0, 1.0),
        ]);

        // Some of it is talk, most of it is the build. The number is honest either way.
        Assert.True(mixed.Assurance > 0.5);
        Assert.True(mixed.Assurance < 1.0);
    }

    [Fact]
    public void Nothing_produced_means_nothing_believed()
    {
        var result = MasteryCredit.Compute(Contract, []);

        Assert.Equal(0.0, result.MasteryCredit);
        Assert.False(result.IsHeld);
        Assert.Equal(0.0, result.Assurance);
    }

    [Fact]
    public void The_computation_is_pure_and_order_independent()
    {
        // A rebuild replays evidence in log order. If the answer depended on the order, the Twin
        // would not be a projection.
        var evidence = new List<AcceptedEvidenceRecord>
        {
            new(EvidenceType.Apply, 0.9, 0.95),
            new(EvidenceType.Explain, 0.8, 0.6),
            new(EvidenceType.Apply, 1.0, 1.0),
            new(EvidenceType.Recall, 1.0, 1.0),
        };

        var forwards = MasteryCredit.Compute(Contract, evidence);
        var backwards = MasteryCredit.Compute(Contract, [.. Enumerable.Reverse(evidence)]);

        Assert.Equal(forwards, backwards);
    }
}
