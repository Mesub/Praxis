using System.Reflection;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Tests;

/// **"Hints cost. Production earns."** (CANON §1.1)
///
/// This is the half everybody forgets, and leaving it out does not make the product stricter — it
/// makes it *broken*. A budget that only ever falls means every learner eventually hits zero and
/// stays there, and a system that has stopped helping you and will not let you earn your way back is
/// not rigorous. It has simply given up on you while claiming to have standards.
///
/// The asymmetry is the design: a learner who **produces** climbs the ladder indefinitely and never
/// goes broke. A learner who only **consumes** is broke in about four rungs. That is I-5, expressed
/// as arithmetic rather than as a warning.
public sealed class ProductionEarnsTests
{
    private static AnswerBudget Budget(int allowance = AnswerBudget.DefaultAllowance)
        => AnswerBudget.For(Guid.CreateVersion7(), Guid.CreateVersion7(), allowance);

    // ── Producing pays ───────────────────────────────────────────────────────

    [Fact]
    public void Producing_something_buys_back_budget()
    {
        var budget = Budget();

        budget.Spend(HintRung.WorkedExample);   // −28

        Assert.Equal(92, budget.Remaining);

        budget.Earn(ProductionKind.SelfExplanation);   // +20

        Assert.Equal(112, budget.Remaining);
        Assert.Equal(20, budget.Earned);
        Assert.Equal(1, budget.ProductionsRecorded);
    }

    /// **The whole economics, in one test.**
    ///
    /// Two learners. One climbs the ladder and *produces at each step*; the other climbs it and
    /// produces nothing. The first can keep going forever. The second is broke.
    ///
    /// Neither of them ever gets the answer — but only one of them is stuck.
    [Fact]
    public void A_learner_who_produces_never_goes_broke_and_a_learner_who_only_consumes_does()
    {
        var producer = Budget();
        var consumer = Budget();

        HintRung[] ladder =
        [
            HintRung.Probe, HintRung.Analogy, HintRung.WorkedExample, HintRung.PartialScaffold,
        ];

        foreach (var rung in ladder)
        {
            producer.Spend(rung);

            // They actually did something with each hint: an explanation, a real attempt.
            producer.Earn(ProductionKind.SelfExplanation);
            producer.Earn(ProductionKind.GenuineWrongAttempt);

            consumer.Spend(rung);   // ...and did nothing.
        }

        Assert.False(
            producer.IsExhausted,
            "A learner who produced at every single step still went broke. The ladder is now a "
            + "trap, and the only winning move is to stop asking for help.");

        Assert.True(producer.CanAfford(HintRung.PartialScaffold));

        // The consumer walked the whole ladder once, and that is all they get.
        Assert.Equal(93, consumer.Spent);
        Assert.False(consumer.CanAfford(HintRung.PartialScaffold));
    }

    // ── We pay you to fail properly ──────────────────────────────────────────

    /// **The cheapest reward here, and the one I would defend hardest.**
    ///
    /// A genuine, reasoned, *wrong* answer earns budget. Not much — 8 — but it earns.
    ///
    /// It has to. The moment failure is free but unpaid, the rational move for a learner is to stop
    /// trying and wait for a hint: guessing costs nothing, thinking costs effort, and neither pays.
    /// Every other product in this industry punishes a wrong answer, because a red cross is easier
    /// to build than a judgement about whether somebody was actually *thinking*.
    [Fact]
    public void A_real_reasoned_wrong_answer_earns_budget()
    {
        var budget = Budget();

        budget.Spend(HintRung.Probe);   // −6

        var earned = budget.Earn(ProductionKind.GenuineWrongAttempt);

        Assert.Equal(8, earned);
        Assert.True(budget.Remaining > AnswerBudget.DefaultAllowance - 6);
    }

    [Fact]
    public void Failing_properly_earns_less_than_succeeding_but_it_is_never_nothing()
    {
        Assert.True(
            ProductionValue.Of(ProductionKind.GenuineWrongAttempt) > 0,
            "A genuine wrong attempt is worth nothing. A learner who works this out will stop "
            + "attempting and start waiting, which is exactly what they should do under those rules.");

        Assert.True(
            ProductionValue.Of(ProductionKind.GenuineWrongAttempt)
            < ProductionValue.Of(ProductionKind.CorrectUnaided));
    }

    /// **Catching your own error is worth more than being told about it.**
    ///
    /// Metacognition is the single most valuable behaviour in this product: a learner who can hear
    /// their own reasoning creak does not need PRAXIS for very much longer — which is precisely the
    /// outcome it is optimising for (I-10). It is worth more than simply being right.
    [Fact]
    public void Catching_your_own_error_pays_more_than_getting_it_right_first_time()
    {
        Assert.True(
            ProductionValue.Of(ProductionKind.CaughtOwnError)
            > ProductionValue.Of(ProductionKind.CorrectUnaided));
    }

    /// Teaching it to someone else is the strongest evidence there is that you hold it — and it is
    /// the most PRAXIS will pay for anything.
    [Fact]
    public void Teaching_it_back_is_the_most_valuable_thing_a_learner_can_do()
    {
        var richest = Enum.GetValues<ProductionKind>().MaxBy(ProductionValue.Of);

        Assert.Equal(ProductionKind.TeachBack, richest);
    }

    [Fact]
    public void Every_production_is_worth_something()
    {
        foreach (var kind in Enum.GetValues<ProductionKind>())
        {
            Assert.True(
                ProductionValue.Of(kind) > 0,
                $"{kind} is worth nothing. A learner who produces for nothing learns quickly not "
                + "to produce.");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionValue.Of((ProductionKind)99));
    }

    // ── The cap ──────────────────────────────────────────────────────────────

    /// **You cannot bank a fortune against the day you decide to stop.**
    ///
    /// Without a ceiling, a strong learner front-loads a hundred teach-backs on an easy concept and
    /// then buys their way straight down the ladder on a hard one, unaided-ness intact on paper and
    /// gone in fact.
    [Fact]
    public void Budget_is_capped_no_matter_how_much_is_produced()
    {
        var budget = Budget();

        for (var i = 0; i < 100; i++)
        {
            budget.Earn(ProductionKind.TeachBack);
        }

        Assert.Equal(budget.Ceiling, budget.Remaining);
        Assert.Equal(168, budget.Remaining);   // 1.4 × 120
    }

    [Fact]
    public void Production_above_the_cap_is_simply_not_credited()
    {
        var budget = Budget();

        while (budget.Remaining < budget.Ceiling)
        {
            budget.Earn(ProductionKind.TeachBack);
        }

        var wasted = budget.Earn(ProductionKind.TeachBack);

        Assert.Equal(0, wasted);
        Assert.Equal(budget.Ceiling, budget.Remaining);
    }

    /// **Mastery in one place is not currency to buy answers in another.**
    ///
    /// The budget is per-concept and so is the earning. A learner who is brilliant at tokenisation
    /// does not thereby get to be walked down the ladder for free on HNSW — and if they could, the
    /// Ledger's claim about HNSW would be worth nothing.
    [Fact]
    public void Surplus_does_not_roll_over_to_another_concept()
    {
        var learner = Guid.CreateVersion7();

        var easy = AnswerBudget.For(learner, Guid.CreateVersion7());
        var hard = AnswerBudget.For(learner, Guid.CreateVersion7());

        for (var i = 0; i < 20; i++)
        {
            easy.Earn(ProductionKind.TeachBack);
        }

        Assert.Equal(easy.Ceiling, easy.Remaining);

        // The hard concept knows nothing about any of it.
        Assert.Equal(AnswerBudget.DefaultAllowance, hard.Remaining);
        Assert.Equal(0, hard.Earned);
    }

    // ── Earned is not bought ─────────────────────────────────────────────────

    /// **The distinction that makes this safe**, and the correction to what I wrote in M6.1.
    ///
    /// My first version of the budget forbade *any* way to add to it, which banned half of I-5. The
    /// real line is **bought vs earned**:
    ///
    /// * *Bought* — an ad, a purchase, an admin override — yields budget **without producing
    ///   anything**, which is what opens the second lock on the oracle-grinding attack.
    /// * *Earned* — a rubric passed, an error caught, a real attempt made — is safe **for exactly
    ///   the reason buying is not**: it requires the one thing a grinder cannot fake.
    ///
    /// You cannot pass a rubric by asking eleven times.
    [Fact]
    public void Budget_can_be_earned_but_never_bought()
    {
        var mutators = typeof(AnswerBudget)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(AnswerBudget))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Earn", mutators);

        string[] bought = ["Grant", "Refill", "TopUp", "Purchase", "Buy", "Award", "Gift", "Override"];

        foreach (var name in bought)
        {
            Assert.DoesNotContain(name, mutators);
        }
    }

    /// `Earn` takes a **`ProductionKind`** and nothing else — no amount.
    ///
    /// If it took an `int`, the caller would name its own price, and a caller under pressure names a
    /// generous one. The event says *what the learner made*; the value is decided here, from a table
    /// anybody can read.
    [Fact]
    public void A_caller_cannot_name_its_own_price()
    {
        var earn = typeof(AnswerBudget).GetMethod(
            "Earn", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var parameters = earn.GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(ProductionKind), parameters[0].ParameterType);
    }
}
