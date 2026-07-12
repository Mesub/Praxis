using Praxis.Modules.Compiler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Tests;

/// The edge weight of the Compiler's graph. Everything the plan promises a learner rests on this
/// number being honest.
public sealed class ConceptCostTests
{
    /// A typical pack contract: talking gets you close, and never over the line.
    private static readonly MasteryContract Typical = new(
        HoldThreshold: 0.80,
        Clauses:
        [
            new EvidenceClause(EvidenceType.Explain, 0.7, 0.25, 1),
            new EvidenceClause(EvidenceType.Apply, 0.8, 0.60, 2),
            new EvidenceClause(EvidenceType.Build, 1.0, 0.85, 1),
        ]);

    /// The gentlest thing a concept can ask for.
    private static readonly MasteryContract Gentle = new(
        HoldThreshold: 0.75,
        Clauses:
        [
            new EvidenceClause(EvidenceType.Recall, 0.9, 0.15, 1),
            new EvidenceClause(EvidenceType.Apply, 0.8, 0.80, 1),
        ]);

    [Fact]
    public void A_fresh_concept_costs_roughly_what_the_pack_says_it_does()
    {
        var minutes = ConceptCost.Minutes(Inputs(estimatedMinutes: 60, contract: Gentle));

        // The pack's estimate, adjusted for a median learner and a gentle contract. It should not
        // be wildly different from the number a domain expert wrote down.
        Assert.InRange(minutes, 30, 150);
    }

    // ── What is already in the cache ─────────────────────────────────────────

    [Fact]
    public void Progress_makes_a_concept_cheaper()
    {
        var fresh = ConceptCost.Minutes(Inputs(masteryCredit: 0.0));
        var halfway = ConceptCost.Minutes(Inputs(masteryCredit: 0.5));

        Assert.True(halfway < fresh);
    }

    /// **The last stretch is the expensive part**, and the discount must be sublinear or the plan
    /// will promise a finish that never arrives.
    ///
    /// A learner 80% of the way to a concept has done the reading and the explaining. What is left
    /// is the never-seen problem and the build that runs — and the Evidence Ceiling means they
    /// cannot get there by explaining more. Pricing the remainder at 20% of the work is exactly how
    /// a roadmap promises twelve weeks and takes thirty.
    [Fact]
    public void Being_eighty_percent_there_does_not_make_it_eighty_percent_cheaper()
    {
        var fresh = ConceptCost.Minutes(Inputs(masteryCredit: 0.0));
        var nearlyThere = ConceptCost.Minutes(Inputs(masteryCredit: 0.8));

        var discount = 1.0 - (nearlyThere / fresh);

        Assert.True(discount < 0.7, $"Being 80% there discounted the cost by {discount:P0}. The "
            + "remaining work is the never-seen problem and the build that runs — the part a learner "
            + "cannot talk their way through.");

        Assert.True(discount > 0.2, "Progress bought them nothing at all.");
    }

    [Fact]
    public void A_concept_already_held_costs_almost_nothing_to_re_reach()
    {
        var minutes = ConceptCost.Minutes(Inputs(masteryCredit: 1.0));

        Assert.Equal(0.0, minutes, precision: 6);
    }

    // ── The strictness of the standard ───────────────────────────────────────

    /// A concept satisfied by recalling a definition and one demanding two never-seen problems plus
    /// code that passes hidden tests are **not the same amount of work**, and pricing them the same
    /// is how roadmaps lie.
    [Fact]
    public void A_concept_that_demands_a_build_costs_more_than_one_that_demands_recall()
    {
        var gentle = ConceptCost.Minutes(Inputs(contract: Gentle));
        var strict = ConceptCost.Minutes(Inputs(contract: Typical));

        Assert.True(strict > gentle * 1.5,
            $"A contract demanding two applied problems and a build ({strict:F0} min) barely cost "
            + $"more than one satisfied by recall ({gentle:F0} min).");
    }

    [Fact]
    public void Explaining_never_makes_a_concept_expensive()
    {
        // Model-graded evidence is cheap to produce and, by the Floor, never sufficient. It cannot
        // be the thing that makes a concept costly — otherwise the Compiler would budget time for
        // talking, which is the one activity that provably does not finish anything.
        var withExplain = new MasteryContract(0.80,
        [
            new EvidenceClause(EvidenceType.Explain, 0.7, 0.25, 5),
            new EvidenceClause(EvidenceType.Build, 1.0, 0.85, 1),
        ]);

        var withoutExplain = new MasteryContract(0.80,
        [
            new EvidenceClause(EvidenceType.Build, 1.0, 0.85, 1),
        ]);

        Assert.Equal(
            ConceptCost.Strictness(withoutExplain),
            ConceptCost.Strictness(withExplain),
            precision: 9);
    }

    [Fact]
    public void Demanding_two_applied_problems_costs_more_than_demanding_one()
    {
        var one = new MasteryContract(0.80, [new EvidenceClause(EvidenceType.Apply, 0.8, 0.85, 1)]);
        var two = new MasteryContract(0.80, [new EvidenceClause(EvidenceType.Apply, 0.8, 0.85, 2)]);

        Assert.True(ConceptCost.Strictness(two) > ConceptCost.Strictness(one));
    }

    [Fact]
    public void A_higher_bar_costs_more()
    {
        var lenient = new MasteryContract(0.60, [new EvidenceClause(EvidenceType.Build, 1.0, 0.9, 1)]);
        var demanding = new MasteryContract(0.95, [new EvidenceClause(EvidenceType.Build, 1.0, 0.99, 1)]);

        Assert.True(ConceptCost.Strictness(demanding) > ConceptCost.Strictness(lenient));
    }

    // ── The wall ─────────────────────────────────────────────────────────────

    /// **The part no other roadmap tool has.**
    ///
    /// A learner attempting a concept whose prerequisites they have half-forgotten does not
    /// experience it as "harder". They experience it as *impossible*, and they blame themselves.
    /// The Compiler must price that, so the Scheduler can refuse to walk them into it.
    [Fact]
    public void Rotting_prerequisites_make_a_concept_far_more_expensive()
    {
        var solid = ConceptCost.Minutes(Inputs(prerequisites: [0.95, 0.92, 0.98]));
        var dissolving = ConceptCost.Minutes(Inputs(prerequisites: [0.95, 0.25, 0.98]));

        Assert.True(
            dissolving > solid * 1.5,
            $"A concept resting on a prerequisite the learner has largely forgotten ({dissolving:F0} "
            + $"min) barely cost more than one resting on solid ground ({solid:F0} min). PRAXIS is "
            + "about to walk someone into a wall and let them think it is their fault.");
    }

    /// **The weakest prerequisite governs, not the average.**
    ///
    /// One dissolved foundation is enough. A learner who has forgotten embeddings cannot do vector
    /// retrieval, however fresh their tokenisation is — and averaging would let strong prerequisites
    /// hide a fatal one.
    [Fact]
    public void One_dissolved_foundation_is_enough_and_averaging_would_hide_it()
    {
        // Same mean retrievability (0.75), very different situations.
        var evenlyWorn = ConceptCost.DecayPressure([0.75, 0.75, 0.75]);
        var oneGone = ConceptCost.DecayPressure([1.0, 1.0, 0.25]);

        Assert.True(
            oneGone > evenlyWorn,
            "Three prerequisites at 75% cost more than two perfect ones and one that is gone. The "
            + "cost function is averaging, and a strong prerequisite is hiding a fatal one.");
    }

    [Fact]
    public void Fresh_prerequisites_cost_nothing_extra()
    {
        Assert.Equal(1.0, ConceptCost.DecayPressure([1.0, 1.0, 1.0]), precision: 9);
    }

    [Fact]
    public void A_root_concept_has_nothing_rotting_beneath_it()
    {
        Assert.Equal(1.0, ConceptCost.DecayPressure([]), precision: 9);
    }

    [Fact]
    public void The_decay_penalty_is_bounded()
    {
        // Even total amnesia does not make a concept infinitely expensive — the learner would
        // simply relearn the prerequisite, and the Compiler's job is to route them there, not to
        // report an unreachable number.
        var totalAmnesia = ConceptCost.DecayPressure([0.0]);

        Assert.InRange(totalAmnesia, 2.0, 6.0);
    }

    // ── The learner's own difficulty ─────────────────────────────────────────

    [Fact]
    public void A_concept_this_learner_finds_hard_costs_them_more()
    {
        // Not the graph's opinion of the concept. How hard *this person* has found it, revealed by
        // what they actually did.
        var easyForThem = ConceptCost.Minutes(Inputs(learnerDifficulty: 2.0));
        var hardForThem = ConceptCost.Minutes(Inputs(learnerDifficulty: 9.0));

        Assert.True(hardForThem > easyForThem * 1.3);
    }

    // ── Sanity ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cost_is_never_negative()
    {
        foreach (var credit in new[] { 0.0, 0.5, 1.0 })
        {
            foreach (var difficulty in new[] { 1.0, 5.0, 10.0 })
            {
                foreach (var prereq in new[] { 0.0, 0.5, 1.0 })
                {
                    var minutes = ConceptCost.Minutes(Inputs(
                        masteryCredit: credit,
                        learnerDifficulty: difficulty,
                        prerequisites: [prereq]));

                    Assert.True(minutes >= 0, $"Negative cost at credit={credit}, d={difficulty}, r={prereq}.");
                }
            }
        }
    }

    [Fact]
    public void The_cost_function_is_pure()
    {
        // The plan is a solver's output, not a prompt's. If this read a clock or rolled a die, two
        // compiles of the same learner would disagree and neither would be trustworthy.
        var inputs = Inputs(masteryCredit: 0.3, prerequisites: [0.8, 0.6]);

        Assert.Equal(ConceptCost.Minutes(inputs), ConceptCost.Minutes(inputs), precision: 12);
    }

    private static ConceptCostInputs Inputs(
        int estimatedMinutes = 60,
        double masteryCredit = 0.0,
        double learnerDifficulty = 5.0,
        MasteryContract? contract = null,
        IReadOnlyList<double>? prerequisites = null)
        => new(
            Guid.CreateVersion7(),
            estimatedMinutes,
            masteryCredit,
            learnerDifficulty,
            contract ?? Typical,
            prerequisites ?? []);
}
