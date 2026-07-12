using Praxis.Modules.Assessment.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **It is not trying to score you. It is trying to find where you break.**
public sealed class ItemSelectionTests
{
    private static Candidate Item(
        string id,
        double b,
        double a = 1.2,
        double cost = 1.0,
        double exposure = 0.0,
        params string[] diagnoses)
        => new(id, a, b, EvidenceType.Apply, cost, diagnoses.ToHashSet(StringComparer.Ordinal), exposure);

    private static SelectionContext Context(
        double theta = 0.0,
        double se = 0.5,
        double hold = 0.0,
        Dictionary<string, double>? misconceptions = null,
        double fatigue = 0.0)
        => new(new Ability(theta, se), hold, misconceptions ?? [], fatigue);

    // ── Decision, not θ ──────────────────────────────────────────────────────

    /// **A merely-hard item is not automatically a good item.**
    ///
    /// An item far above the contract threshold sharpens θ beautifully and tells you **nothing about
    /// the decision** — you already knew they would probably fail it. The learner's afternoon paid
    /// for a number nobody needed.
    [Fact]
    public void An_item_at_the_threshold_beats_one_that_only_sharpens_theta()
    {
        // The learner sits well above the contract threshold. The question is whether they *hold*
        // the concept — and that question is settled near the threshold, not out in the wilderness.
        var context = Context(theta: 1.5, se: 0.6, hold: 0.0);

        var atTheDecision = ItemSelection.DecisionInformation(Item("at-threshold", b: 0.0), context);
        var milesAway = ItemSelection.DecisionInformation(Item("miles-away", b: 3.5), context);

        Assert.True(
            atTheDecision > milesAway,
            "An item far from the contract threshold is being valued as highly as one that would "
            + "actually settle the decision. PRAXIS is measuring ability instead of answering the "
            + "question it was asked.");
    }

    /// **The whole point of the decision term.** We do not need θ to three decimals — we need to know
    /// whether they hold it, in as few items as possible. Every item saved is a minute given back.
    [Fact]
    public void Certainty_about_the_decision_is_worth_nothing_more_to_buy()
    {
        // PRAXIS is already *sure* this learner holds the concept.
        var settled = Context(theta: 3.0, se: 0.2, hold: 0.0);

        // ...and genuinely unsure about this one.
        var undecided = Context(theta: 0.0, se: 0.8, hold: 0.0);

        var buyingSettled = ItemSelection.DecisionInformation(Item("i", b: 0.0), settled);
        var buyingUndecided = ItemSelection.DecisionInformation(Item("i", b: 0.0), undecided);

        Assert.True(
            buyingUndecided > buyingSettled,
            "PRAXIS is spending a learner's time re-confirming something it already knows.");
    }

    [Fact]
    public void Entropy_is_one_bit_at_a_coin_flip_and_zero_at_certainty()
    {
        Assert.Equal(1.0, ItemSelection.Entropy(0.5), tolerance: 1e-9);

        Assert.Equal(0.0, ItemSelection.Entropy(0.0));
        Assert.Equal(0.0, ItemSelection.Entropy(1.0));

        Assert.True(ItemSelection.Entropy(0.9) < 0.5);
    }

    // ── The hunt (I-6) ───────────────────────────────────────────────────────

    /// **The λ_m term is Invention I-6, expressed as one line of arithmetic.**
    ///
    /// The engine will deliberately choose a *slightly less informative* item about θ if that item
    /// can resolve an open misconception. It is not trying to measure the learner. **It is trying to
    /// find where they break.**
    ///
    /// ## And the first version of this test claimed more than the design does
    ///
    /// I made the diagnostic item *drastically* less informative — a sharp, perfectly-matched item
    /// against a woolly, badly-offset one — and asserted the diagnostic would still win. It did not,
    /// and it **should not have**.
    ///
    /// CANON says *slightly* less informative, and it means it. PRAXIS will pay a real price to find
    /// where somebody breaks; it will not pay **any** price. An engine that would chase a
    /// misconception with an item that measures nothing has stopped assessing altogether — it is
    /// just following a hunch, and spending a learner's afternoon on it.
    ///
    /// So this now tests the actual claim, and the test below it tests the limit.
    [Fact]
    public void A_less_informative_item_wins_if_it_can_find_where_they_break()
    {
        var context = Context(
            theta: 0.0,
            se: 0.5,
            hold: 0.0,
            misconceptions: new() { ["thinks-tf-is-linear"] = 0.5 });   // a genuine coin flip

        // The better item, measured purely by information about θ.
        var sharper = Item("measures-them-precisely", b: 0.0, a: 1.5);

        // Genuinely worse for θ — about 40% of the information — but it would settle the
        // misconception.
        var diagnostic = Item(
            "finds-where-they-break", b: 0.8, a: 1.0, diagnoses: "thinks-tf-is-linear");

        Assert.True(
            Irt.Information(0.0, 1.5, 0.0) > Irt.Information(0.0, 1.0, 0.8),
            "The premise of this test is wrong: the 'sharper' item is not actually sharper.");

        var chosen = ItemSelection.Choose([sharper, diagnostic], context);

        Assert.Equal("finds-where-they-break", chosen.Value.Id);
    }

    /// **PRAXIS will pay a real price to find where you break. It will not pay any price.**
    ///
    /// An engine that would chase a misconception with an item that measures nothing has stopped
    /// assessing altogether. It is following a hunch, and spending a learner's afternoon on it.
    [Fact]
    public void The_hunt_has_a_budget_and_a_useless_item_does_not_win()
    {
        var context = Context(misconceptions: new() { ["m"] = 0.5 });

        var good = Item("measures-something", b: 0.0, a: 2.0);

        // Diagnoses the misconception — and is so far off-target and so woolly that it tells you
        // essentially nothing about whether this learner can do anything.
        var useless = Item("measures-nothing", b: 3.8, a: 0.3, diagnoses: "m");

        var chosen = ItemSelection.Choose([good, useless], context);

        Assert.Equal(
            "measures-something",
            chosen.Value.Id);
    }

    /// **Certainty is not a thing you buy twice.** A misconception PRAXIS is already sure about — in
    /// either direction — is worth nothing to probe.
    [Fact]
    public void A_settled_misconception_is_not_worth_probing()
    {
        var item = Item("probe", b: 0.0, diagnoses: "settled");

        var unsure = Context(misconceptions: new() { ["settled"] = 0.5 });
        var certain = Context(misconceptions: new() { ["settled"] = 0.99 });

        Assert.True(
            ItemSelection.MisconceptionInformation(item, unsure)
            > ItemSelection.MisconceptionInformation(item, certain));

        Assert.Equal(0.0, ItemSelection.MisconceptionInformation(item, Context()), tolerance: 1e-9);
    }

    [Fact]
    public void An_item_that_diagnoses_nothing_gets_no_hunting_bonus()
    {
        var context = Context(misconceptions: new() { ["open"] = 0.5 });

        Assert.Equal(
            0.0,
            ItemSelection.MisconceptionInformation(Item("blind", b: 0.0), context),
            tolerance: 1e-9);
    }

    // ── The penalties ────────────────────────────────────────────────────────

    /// **A BUILD costs twenty minutes; an APPLY costs forty seconds.** An engine that ignores that
    /// will fill a fifteen-minute session with one item and call it efficient.
    [Fact]
    public void An_expensive_item_has_to_be_worth_it()
    {
        var context = Context();

        var cheap = Item("cheap", b: 0.0, cost: 0.7);
        var expensive = Item("expensive", b: 0.0, cost: 20.0);

        Assert.True(ItemSelection.Utility(cheap, context) > ItemSelection.Utility(expensive, context));
    }

    /// **An item everybody sees is an item everybody has heard about.** Sympson–Hetter.
    [Fact]
    public void An_over_exposed_item_is_worth_less()
    {
        var context = Context();

        var fresh = Item("fresh", b: 0.0, exposure: 0.0);
        var everywhere = Item("everywhere", b: 0.0, exposure: 1.0);

        Assert.True(ItemSelection.Utility(fresh, context) > ItemSelection.Utility(everywhere, context));
    }

    [Fact]
    public void A_tired_learner_is_not_handed_the_twenty_minute_item()
    {
        var fresh = Context(fatigue: 0.0);
        var tired = Context(fatigue: 1.0);

        var expensive = Item("build", b: 0.0, cost: 20.0);

        Assert.True(ItemSelection.Utility(expensive, tired) < ItemSelection.Utility(expensive, fresh));
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    /// **PRAXIS will not invent an item to keep the session moving.** A made-up item produces made-up
    /// evidence, and made-up evidence is indistinguishable from the real thing once it is in the log.
    [Fact]
    public void With_nothing_to_ask_PRAXIS_says_so_rather_than_inventing_something()
    {
        var refused = ItemSelection.Choose([], Context());

        Assert.False(refused.IsSuccess);
        Assert.Contains("made-up evidence", refused.Message!, StringComparison.Ordinal);
    }

    /// The same learner, in the same state, gets the same item. **A selector that says something
    /// different each time you look at it is not selecting. It is stalling.**
    [Fact]
    public void The_same_learner_in_the_same_state_gets_the_same_item()
    {
        var context = Context(misconceptions: new() { ["m"] = 0.5 });

        List<Candidate> candidates =
        [
            Item("a", 0.0), Item("b", 0.1), Item("c", -0.1, diagnoses: "m"),
        ];

        var first = ItemSelection.Choose(candidates, context);
        var second = ItemSelection.Choose(candidates, context);

        Assert.Equal(first.Value.Id, second.Value.Id);
    }
}
