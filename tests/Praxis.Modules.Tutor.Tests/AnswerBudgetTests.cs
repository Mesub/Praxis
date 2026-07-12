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

        Assert.True(budget.Spend(HintRung.Partial).IsSuccess);

        Assert.Equal(2, budget.Spent);
        Assert.Equal(3, budget.Remaining);
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
        HintRung[] ladder = [HintRung.Orient, HintRung.Probe, HintRung.Analogy, HintRung.Partial, HintRung.Full];

        var costs = ladder.Select(HintCost.Of).ToList();

        Assert.Equal(costs.OrderBy(c => c), costs);

        Assert.True(
            HintCost.Of(HintRung.Full) > HintCost.Of(HintRung.Probe),
            "The full scaffold costs no more than a probe. A learner who notices will never probe "
            + "again, and the ladder is now a button.");
    }

    // ── It runs out, and that is the point ───────────────────────────────────

    [Fact]
    public void When_the_budget_is_gone_PRAXIS_says_so_and_helps_no_further()
    {
        var budget = Budget();

        Assert.True(budget.Spend(HintRung.Full).IsSuccess);      // 3
        Assert.True(budget.Spend(HintRung.Partial).IsSuccess);   // 2 → 5 of 5

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
        var budget = Budget(allowance: 2);

        var refused = budget.Spend(HintRung.Full);   // costs 3

        Assert.False(refused.IsSuccess);

        // Nothing was spent, nothing was recorded, and no hint was given.
        Assert.Equal(0, budget.Spent);
        Assert.Equal(0, budget.HintsTaken);
        Assert.Null(budget.DeepestRung);
        Assert.Equal(2, budget.Remaining);
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
        Assert.True(budget.IsExhausted || !budget.CanAfford(HintRung.Full));
    }

    [Fact]
    public void CanAfford_answers_before_the_tutor_commits_to_helping()
    {
        var budget = Budget(allowance: 2);

        Assert.True(budget.CanAfford(HintRung.Probe));    // 1
        Assert.True(budget.CanAfford(HintRung.Partial));  // 2
        Assert.False(budget.CanAfford(HintRung.Full));    // 3

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
        budget.Spend(HintRung.Partial);
        budget.Spend(HintRung.Probe);

        Assert.Equal(HintRung.Partial, budget.DeepestRung);
        Assert.Equal(3, budget.HintsTaken);
    }

    // ── There is no way to buy more. Asserted, not promised. ─────────────────

    /// **The test that stops this becoming a hint shop.**
    ///
    /// Every commercial instinct points at a refill: *out of hints? Watch an ad. Buy a pack. Come
    /// back tomorrow for three free ones.* Each would work, and each would open the second lock — the
    /// one stopping a patient learner from grinding the oracle until it gives up the answer.
    ///
    /// So the type has exactly one mutator and it only ever subtracts. If a `Grant`, a `Refill`, an
    /// `AddHints` or a `Reset(int)` ever appears, this fails.
    [Fact]
    public void There_is_no_way_to_add_to_a_budget()
    {
        var mutators = typeof(AnswerBudget)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AnswerBudget))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] forbidden =
        [
            "Grant", "Refill", "Add", "AddHints", "Increase", "TopUp", "Purchase", "Award", "Extend",
        ];

        foreach (var name in forbidden)
        {
            Assert.False(
                mutators.Contains(name),
                $"AnswerBudget.{name} exists. The budget can now be refilled — and a budget that "
                + "can be refilled is not a budget, it is a price. The learner can buy their way "
                + "past the friction that was the entire point, and grind the oracle while they "
                + "are at it.");
        }

        // Spend (which subtracts) and Reset (which a projection rebuild needs, and which sets it
        // back to *zero spent*, not to a full purse) are the only two.
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

        budget.Spend(HintRung.Full);
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
