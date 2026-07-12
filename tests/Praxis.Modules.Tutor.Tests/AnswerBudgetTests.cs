using System.Reflection;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Tests;

/// **The Answer Budget is doing two jobs, and both of them need it to be genuinely scarce.**
///
/// It is *pedagogy*: unlimited hints do not accelerate learning, they replace it. And it is a
/// *security control*: the tutor cannot give the answer (it is not in its context), but it can ask
/// the oracle whether a claim is right — and given unlimited questions, a patient learner can grind
/// guesses against that oracle and binary-search the answer out.
///
/// Novelty (M5.5) stops you grinding *one* answer. The budget stops you grinding *around* it. Sell a
/// refill and the second lock opens.
public sealed class AnswerBudgetTests
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bm25 = Guid.CreateVersion7();

    private static AnswerBudget Budget(int allowance = AnswerBudget.DefaultAllowance)
        => AnswerBudget.For(Alice, Bm25, allowance);

    // ── It spends ────────────────────────────────────────────────────────────

    [Fact]
    public void A_new_learner_has_their_whole_allowance()
    {
        var budget = Budget();

        Assert.Equal(AnswerBudget.DefaultAllowance, budget.Remaining);
        Assert.Equal(0, budget.Spent);
        Assert.False(budget.IsExhausted);
        Assert.Null(budget.DeepestRung);
    }

    [Fact]
    public void Taking_a_hint_costs_what_the_rung_costs()
    {
        var budget = Budget();

        Assert.True(budget.Spend(HintRung.WorkedExample).IsSuccess);

        Assert.Equal(28, budget.Spent);
        Assert.Equal(92, budget.Remaining);
        Assert.Equal(1, budget.HintsTaken);
    }

    /// **Orienting yourself is free.**
    ///
    /// "What kind of problem is this?" is barely a hint — it is a skill, and PRAXIS wants it
    /// practised rather than rationed. Charging for it would teach the learner not to do it.
    [Fact]
    public void Orienting_costs_nothing_because_it_is_a_skill_not_a_hint()
    {
        var budget = Budget();

        for (var i = 0; i < 20; i++)
        {
            Assert.True(budget.Spend(HintRung.Orient).IsSuccess);
        }

        Assert.Equal(AnswerBudget.DefaultAllowance, budget.Remaining);
        Assert.False(budget.IsExhausted);
    }

    /// **The ladder is not flat, and it must not be.**
    ///
    /// Under a flat cost, a learner optimising against the price jumps straight to the bottom rung
    /// every time — which is exactly the behaviour the ladder exists to prevent. Climbing down
    /// slowly is cheap. Demanding the strongest scaffold immediately is not.
    [Fact]
    public void The_deepest_rung_costs_the_most()
    {
        var costs = Enum.GetValues<HintRung>().Select(HintCost.Of).ToList();

        Assert.Equal(costs.OrderBy(c => c), costs);

        Assert.True(
            HintCost.Of(HintRung.PartialScaffold) > HintCost.Of(HintRung.Probe),
            "The strongest scaffold costs no more than a probe. A learner who notices will never "
            + "probe again, and the ladder is now a button.");
    }

    // ── It runs out, and that is the point ───────────────────────────────────

    [Fact]
    public void When_the_budget_is_gone_PRAXIS_says_so_and_helps_no_further()
    {
        var budget = Budget(allowance: 45);

        Assert.True(budget.Spend(HintRung.PartialScaffold).IsSuccess);   // 45 of 45

        Assert.True(budget.IsExhausted);
        Assert.Equal(0, budget.Remaining);

        var refused = budget.Spend(HintRung.Probe);

        Assert.False(refused.IsSuccess);
        Assert.Contains("helped as much as it is going to", refused.Message!, StringComparison.Ordinal);
        Assert.Contains("you produce yourself", refused.Message!, StringComparison.Ordinal);
    }

    /// **A refused hint is not a free hint.**
    ///
    /// The tempting implementation is to clamp the spend at zero and hand the hint over anyway —
    /// "they were nearly there". That is the worst of both worlds: the learner got the help, and the
    /// Ledger says they did not, and the evidence that follows is credited as if they had produced
    /// it alone.
    [Fact]
    public void A_hint_they_cannot_afford_is_refused_rather_than_clamped()
    {
        var budget = Budget(allowance: 20);

        var refused = budget.Spend(HintRung.PartialScaffold);   // costs 45

        Assert.False(refused.IsSuccess);

        // Nothing was spent, nothing was recorded, and no hint was given.
        Assert.Equal(0, budget.Spent);
        Assert.Equal(0, budget.HintsTaken);
        Assert.Null(budget.DeepestRung);
        Assert.Equal(20, budget.Remaining);
    }

    /// The invariant, hammered: whatever sequence of rungs is demanded, in whatever order, the
    /// learner never ends up owing PRAXIS help.
    ///
    /// Note what this test taught me. Its first version asserted that spending `Full` fifty times
    /// leaves you at zero — and it failed with 2 remaining, because after one `Full` (3 of 5) a
    /// second is **refused**, not clamped. That is the design working: the expensive rung becomes
    /// unaffordable while a cheap one is still within reach, which is exactly the right shape for a
    /// learner who has already been walked most of the way down.
    [Fact]
    public void No_sequence_of_demands_can_drive_the_budget_negative()
    {
        HintRung[] every = [.. Enum.GetValues<HintRung>()];

        var budget = Budget();

        for (var i = 0; i < 200; i++)
        {
            budget.Spend(every[i % every.Length]);

            Assert.True(budget.Remaining >= 0, "The learner now owes PRAXIS hints.");
            Assert.True(budget.Spent <= budget.Allowance, "More was spent than was ever allowed.");
        }

        // And it did genuinely run out — this is not passing because nothing was spent.
        Assert.True(budget.IsExhausted || !budget.CanAfford(HintRung.PartialScaffold));
    }

    [Fact]
    public void CanAfford_answers_before_the_tutor_commits_to_helping()
    {
        var budget = Budget(allowance: 14);

        Assert.True(budget.CanAfford(HintRung.Probe));            // 6
        Assert.True(budget.CanAfford(HintRung.Analogy));          // 14
        Assert.False(budget.CanAfford(HintRung.WorkedExample));   // 28

        // And the free rung is always affordable, even at zero.
        var empty = Budget(allowance: 0);
        Assert.True(empty.CanAfford(HintRung.Orient));
    }

    // ── The Ledger is entitled to know how much help they had ────────────────

    [Fact]
    public void The_deepest_rung_taken_is_remembered_because_the_credit_is_discounted_for_it()
    {
        // "You did not produce that alone" (I-5). The Twin discounts the evidence accordingly, and
        // it cannot do that if the budget forgets.
        var budget = Budget();

        budget.Spend(HintRung.Probe);
        budget.Spend(HintRung.WorkedExample);
        budget.Spend(HintRung.Probe);

        Assert.Equal(HintRung.WorkedExample, budget.DeepestRung);
        Assert.Equal(3, budget.HintsTaken);
    }

    // ── There is no way to buy more. Asserted, not promised. ─────────────────

    /// **The test that stops this becoming a hint shop.**
    ///
    /// ## And the correction it needed
    ///
    /// The first version of this test forbade *any* way to add to a budget, and it was wrong.
    ///
    /// CANON §1.1 is one line: **"Hints cost. Production earns."** Budget is meant to come back —
    /// you *earn* it by producing (M6.2b), and PRAXIS even pays you for a genuine wrong attempt,
    /// because productive failure is not free and it deserves funding. A learner who produces at the
    /// expected rate can climb the ladder indefinitely; a learner who only consumes is broke in four
    /// rungs. **That asymmetry is I-5**, and my absolutist version had banned half of it.
    ///
    /// The real line is not *added* vs *not added*. It is **bought** vs **earned**:
    ///
    /// * **Bought** — an ad, a purchase, a support ticket, an admin override. This is what opens the
    ///   second lock on the oracle-grinding attack, because it lets a learner acquire budget
    ///   *without producing anything*. Forbidden, and asserted below.
    /// * **Earned** — evidence that passed a rubric, a correct answer reached unaided, a wrong
    ///   answer that was genuinely reasoned. Safe *precisely because* it requires production, which
    ///   is the one thing a grinder cannot fake (the Evidence Ceiling, C-4).
    ///
    /// So the ban is on the *purchase* verbs, not on arithmetic.
    [Fact]
    public void A_budget_can_never_be_bought_granted_or_topped_up()
    {
        var mutators = typeof(AnswerBudget)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AnswerBudget))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Every one of these is a way to get budget **without producing anything**, and that is the
        // whole of the objection. `Earn` is deliberately not on this list (M6.2b).
        string[] forbidden =
        [
            "Grant", "Refill", "TopUp", "Purchase", "Buy", "Award", "Gift", "Comp", "Override",
        ];

        foreach (var name in forbidden)
        {
            Assert.False(
                mutators.Contains(name),
                $"AnswerBudget.{name} exists. A learner can now acquire budget without producing "
                + "anything — which is not merely a pricing decision. It is the second lock on the "
                + "oracle-grinding attack, and it is now open.");
        }

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { nameof(AnswerBudget.CanAfford), "Spend", "Reset" },
            mutators);
    }

    [Fact]
    public void Reset_replays_from_zero_and_does_not_hand_out_a_fresh_purse()
    {
        // `Reset` exists for a projection rebuild — it must return the budget to "no history", not
        // to "full allowance and go again".
        var budget = Budget();

        budget.Spend(HintRung.PartialScaffold);
        budget.Reset();

        Assert.Equal(0, budget.Spent);
        Assert.Equal(0, budget.HintsTaken);
        Assert.Null(budget.DeepestRung);

        // The allowance it was created with is unchanged — Reset did not enlarge it.
        Assert.Equal(AnswerBudget.DefaultAllowance, budget.Allowance);
    }

    [Fact]
    public void A_negative_allowance_is_a_debt_not_a_budget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnswerBudget.For(Alice, Bm25, -1));
    }

    /// An unpriced rung would be a free rung, and a free rung is an unlimited one.
    [Fact]
    public void Every_rung_has_a_price()
    {
        foreach (var rung in Enum.GetValues<HintRung>())
        {
            Assert.True(HintCost.Of(rung) >= 0);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => HintCost.Of((HintRung)99));
    }
}
