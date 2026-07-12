using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Tests;

/// **The ladder is a gate, not advice.**
///
/// A language model told to "be Socratic and never give the answer" will be Socratic for about four
/// turns and then, faced with a frustrated human, help. That is not a flaw in the model — it is the
/// model doing exactly what it was trained to do, which is be useful to the person in front of it
/// *right now*. It is *we* who know that the most useful thing for that person, over the eighteen
/// months in which they will not remember this conversation, is to not be helped yet.
///
/// So none of this lives in a prompt. It lives here, where frustration cannot argue with it.
public sealed class HintLadderTests
{
    /// Five rungs, and the sixth one CANON names is simply not here.
    private static readonly string[] TheWholeLadder =
        ["Orient", "Probe", "Analogy", "WorkedExample", "PartialScaffold"];

    private static AnswerBudget Budget(int allowance = AnswerBudget.DefaultAllowance)
        => AnswerBudget.For(Guid.CreateVersion7(), Guid.CreateVersion7(), allowance);

    /// Walk the ladder honestly: serve a rung, let the learner speak, serve the next.
    private static LadderState Climb(params HintRung[] rungs)
    {
        var state = LadderState.Fresh;

        foreach (var rung in rungs)
        {
            state = state.After(new HintGrant(rung, HintCost.Of(rung), false, false))
                         .AfterLearnerSpoke();
        }

        return state;
    }

    // ── The rung that does not exist ─────────────────────────────────────────

    /// **The most important test in this file, and it asserts an absence.**
    ///
    /// CANON §1.2 lists a sixth rung — `L5 FULL_REVEAL` — and marks it *"in LEARN mode this rung
    /// does not exist"*. It does not exist here either: **not as a gated member, not as a member
    /// behind a feature flag, and not as a member that throws.**
    ///
    /// A gated member is a member somebody ungates under deadline, in a sprint where retention is
    /// down and the obvious fix is to let people see the answer once in a while. An absent one is
    /// absent. There is no enum value to reach for, so there is no code path to write.
    [Fact]
    public void There_is_no_rung_that_gives_the_answer()
    {
        var rungs = Enum.GetNames<HintRung>();

        string[] forbidden = ["Full", "FullReveal", "Reveal", "Answer", "Solution", "Tell", "Show"];

        foreach (var name in forbidden)
        {
            Assert.False(
                rungs.Contains(name, StringComparer.OrdinalIgnoreCase),
                $"HintRung.{name} exists. PRAXIS can now be asked for the answer, and every claim "
                + "it makes about its tutor is over — not because someone will call it today, but "
                + "because it is there to be called on the day retention is down.");
        }

        Assert.Equal(TheWholeLadder, rungs);
    }

    /// The deepest rung PRAXIS will ever serve gives you *the structure*. It does not fill the hole.
    [Fact]
    public void The_deepest_rung_is_a_scaffold_and_the_learner_still_has_to_produce()
    {
        var deepest = Enum.GetValues<HintRung>().Max();

        Assert.Equal(HintRung.PartialScaffold, deepest);
    }

    // ── LR-1. No skipping ────────────────────────────────────────────────────

    /// **A learner who skips to the scaffold has not been helped past the difficulty. They have been
    /// carried over it** — and they will meet it again on the next item, unchanged.
    [Fact]
    public void The_scaffold_cannot_be_demanded_before_the_ladder_has_been_climbed()
    {
        var refused = HintLadder.Consider(HintRung.PartialScaffold, LadderState.Fresh, Budget());

        Assert.False(refused.IsSuccess);
        Assert.Contains("carried over it", refused.Message!, StringComparison.Ordinal);

        // And it is not an exhaustion — they have plenty of budget. They just have not earned it.
        Assert.False(HintLadder.IsExhaustion(refused));
    }

    [Fact]
    public void Each_rung_unlocks_exactly_the_next_one()
    {
        var state = Climb(HintRung.Orient);

        Assert.True(HintLadder.Consider(HintRung.Probe, state, Budget()).IsSuccess);
        Assert.False(HintLadder.Consider(HintRung.Analogy, state, Budget()).IsSuccess);
    }

    [Fact]
    public void A_learner_can_walk_the_whole_ladder_when_they_actually_climb_it()
    {
        var budget = Budget();
        var state = LadderState.Fresh;

        HintRung[] ladder =
        [
            HintRung.Orient, HintRung.Probe, HintRung.Analogy,
            HintRung.WorkedExample, HintRung.PartialScaffold,
        ];

        foreach (var rung in ladder)
        {
            var granted = HintLadder.Consider(rung, state, budget);

            Assert.True(granted.IsSuccess, $"{rung} was refused: {granted.Message}");

            budget.Spend(rung);
            state = state.After(granted.Value).AfterLearnerSpoke();
        }

        // 6 + 14 + 28 + 45 = 93 of 120. Walked all the way down, exactly once, with a little left.
        Assert.Equal(93, budget.Spent);
        Assert.Equal(27, budget.Remaining);
    }

    // ── LR-3. The tutor may not monologue ────────────────────────────────────

    /// **A hint delivered to someone who has not said anything yet is not scaffolding. It is a
    /// lecture with a price tag.**
    ///
    /// Without this rule the tutor walks the learner all the way to the bottom of the ladder while
    /// they sit there reading, and they end the session having produced nothing at all.
    [Fact]
    public void The_ladder_does_not_advance_while_the_learner_is_still_thinking()
    {
        var state = LadderState.Fresh
            .After(new HintGrant(HintRung.Orient, 0, false, false));   // learner has not replied

        var refused = HintLadder.Consider(HintRung.Probe, state, Budget());

        Assert.False(refused.IsSuccess);
        Assert.Contains("has not responded", refused.Message!, StringComparison.Ordinal);

        // And the moment they say something, it moves.
        Assert.True(HintLadder.Consider(HintRung.Probe, state.AfterLearnerSpoke(), Budget()).IsSuccess);
    }

    // ── LR-2. The surcharge ──────────────────────────────────────────────────

    /// Re-reading a hint you did not understand is fine. **Wearing the same rung down until it
    /// accidentally says something new is not** — and a learner who discovers that asking eleven
    /// times gets eleven slightly different answers has found a way to grind the tutor.
    [Fact]
    public void Asking_for_the_same_rung_again_costs_more()
    {
        var state = Climb(HintRung.Orient, HintRung.Probe);

        var again = HintLadder.Consider(HintRung.Probe, state, Budget());

        Assert.True(again.IsSuccess);
        Assert.True(again.Value.IsRepeat);

        Assert.Equal(9, again.Value.Cost);   // 6 × 1.5
        Assert.True(again.Value.Cost > HintCost.Of(HintRung.Probe));
    }

    [Fact]
    public void Orienting_yourself_again_is_still_free()
    {
        // Charging for a second orientation would teach exactly the wrong lesson: that working out
        // what you already know is a cost to be minimised.
        var state = Climb(HintRung.Orient);

        var again = HintLadder.Consider(HintRung.Orient, state, Budget());

        Assert.True(again.IsSuccess);
        Assert.Equal(0, again.Value.Cost);
    }

    // ── LR-4. Backing off is never punished ──────────────────────────────────

    /// **A tutor that is charged for retreating will not retreat.** It will only ever escalate — and
    /// escalation is the one direction on this ladder that costs the learner something real.
    [Fact]
    public void Dropping_back_down_the_ladder_is_free()
    {
        var state = Climb(HintRung.Orient, HintRung.Probe, HintRung.Analogy);

        var descent = HintLadder.Consider(HintRung.Probe, state, Budget());

        Assert.True(descent.IsSuccess);
        Assert.True(descent.Value.IsDescent);
        Assert.Equal(0, descent.Value.Cost);
    }

    /// Even at zero budget. A learner who is broke must still be able to be walked *back up* to an
    /// orienting question — that is the one kind of help that is always affordable.
    [Fact]
    public void A_broke_learner_can_still_be_backed_off_to_something_simpler()
    {
        var state = Climb(HintRung.Orient, HintRung.Probe, HintRung.Analogy);

        var broke = Budget(allowance: 0);

        var descent = HintLadder.Consider(HintRung.Orient, state, broke);

        Assert.True(descent.IsSuccess);
        Assert.Equal(0, descent.Value.Cost);
    }

    /// A descent does not lower the high-water mark — otherwise a learner could walk back up the
    /// deep rungs for free, forever.
    [Fact]
    public void Descending_does_not_make_the_deep_rungs_cheap_again()
    {
        var state = Climb(HintRung.Orient, HintRung.Probe, HintRung.Analogy);

        state = state.After(new HintGrant(HintRung.Probe, 0, IsDescent: true, IsRepeat: true))
                     .AfterLearnerSpoke();

        Assert.Equal(HintRung.Analogy, state.DeepestServed);

        // Asking for Analogy again is a *repeat*, at 1.5×. It is not a fresh rung at base price.
        var again = HintLadder.Consider(HintRung.Analogy, state, Budget());

        Assert.True(again.Value.IsRepeat);
        Assert.Equal(21, again.Value.Cost);   // 14 × 1.5
    }

    // ── LR-5. Going broke does not get you the answer ────────────────────────

    /// **The whole thing, in one test.**
    ///
    /// CANON §1.5: *"Not this: 'OK fine, here's the answer.' That path does not exist in the code."*
    ///
    /// Zero budget is a **compile signal, not a surrender**. The item stops, the learner writes down
    /// exactly where their reasoning broke, and the concept comes back — behind the prerequisite
    /// they were missing, or explained a different way. Going broke does not get you the answer. It
    /// gets you a better plan.
    [Fact]
    public void Running_out_of_budget_ends_the_item_and_never_reveals_the_answer()
    {
        var budget = Budget(allowance: 10);   // enough for a Probe (6), not an Analogy (14)

        var state = Climb(HintRung.Orient, HintRung.Probe);

        var refused = HintLadder.Consider(HintRung.Analogy, state, budget);

        Assert.False(refused.IsSuccess);
        Assert.True(HintLadder.IsExhaustion(refused));

        Assert.Contains("not going to tell you the answer", refused.Message!, StringComparison.Ordinal);
        Assert.Contains("where your reasoning breaks", refused.Message!, StringComparison.Ordinal);
    }

    /// The two refusals are **different conversations**, and the caller has to be able to tell them
    /// apart: "you have not earned this yet" is not "you are out of budget".
    [Fact]
    public void An_exhaustion_is_distinguishable_from_a_not_yet()
    {
        var notYet = HintLadder.Consider(HintRung.PartialScaffold, LadderState.Fresh, Budget());

        var exhausted = HintLadder.Consider(
            HintRung.Probe, Climb(HintRung.Orient), Budget(allowance: 0));

        Assert.False(notYet.IsSuccess);
        Assert.False(exhausted.IsSuccess);

        Assert.False(HintLadder.IsExhaustion(notYet));
        Assert.True(HintLadder.IsExhaustion(exhausted));
    }

    // ── The economics ────────────────────────────────────────────────────────

    /// *"Hint costs are roughly one production event apart."* A learner who only **consumes** is
    /// broke in about four rungs. A learner who **produces** climbs indefinitely (M6.2b).
    ///
    /// **That asymmetry is I-5.** It is not a limit on help — it is a limit on *unearned* help.
    [Fact]
    public void A_learner_who_only_consumes_goes_broke_in_about_four_rungs()
    {
        var budget = Budget();
        var state = LadderState.Fresh;

        var rungsBought = 0;

        foreach (var rung in Enum.GetValues<HintRung>())
        {
            var granted = HintLadder.Consider(rung, state, budget);

            if (!granted.IsSuccess)
            {
                break;
            }

            if (granted.Value.Cost > 0)
            {
                rungsBought++;
            }

            budget.Spend(rung);
            state = state.After(granted.Value).AfterLearnerSpoke();
        }

        Assert.Equal(4, rungsBought);
        Assert.True(budget.Remaining < HintCost.Of(HintRung.PartialScaffold));
    }

    [Fact]
    public void The_cost_of_a_rung_rises_with_the_strength_of_the_scaffold()
    {
        var costs = Enum.GetValues<HintRung>().Select(HintCost.Of).ToList();

        Assert.Equal(costs.OrderBy(c => c), costs);

        Assert.Equal(0, HintCost.Of(HintRung.Orient));
        Assert.Equal(45, HintCost.Of(HintRung.PartialScaffold));
    }
}
