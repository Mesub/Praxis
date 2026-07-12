using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **A forfeit is scored, small, and permanent** — the counterweight that stops the Loops board being
/// gamed by attrition.
public sealed class ForfeitRuleTests
{
    /// **Ducking is never the profitable move.** For any baseline, a forfeit scores worse than having
    /// just predicted the baseline — which is what makes "make a hundred predictions, resolve only the
    /// winners" a losing strategy.
    [Theory]
    [InlineData(0.10)]
    [InlineData(0.25)]
    [InlineData(0.50)]
    [InlineData(0.90)]
    public void A_forfeit_scores_worse_than_doing_nothing(double baseline)
    {
        Assert.True(ForfeitRule.IsWorseThanDoingNothing(baseline));
        Assert.True(ForfeitRule.ForfeitBrier(baseline) > baseline);
    }

    /// **But only a little — you are not punished for having a life.** A busy fortnight that eats a
    /// two-week prediction is a missed chance to produce evidence, not a catastrophe.
    [Fact]
    public void The_penalty_is_small_on_purpose()
    {
        Assert.True(ForfeitRule.Penalty > 0.0, "A free forfeit lets you cherry-pick by attrition.");
        Assert.True(
            ForfeitRule.Penalty <= ForfeitRule.MaxDefensiblePenalty,
            "The forfeit penalty has crept up past 'not punished for having a life' into a trap.");
    }

    /// The forfeit is the baseline plus exactly the penalty, clamped to a valid Brier.
    [Fact]
    public void The_forfeit_is_the_baseline_plus_the_penalty()
    {
        Assert.Equal(0.30, ForfeitRule.ForfeitBrier(0.25), tolerance: 1e-9);

        // And never off the end of the scale.
        Assert.Equal(1.0, ForfeitRule.ForfeitBrier(0.99), tolerance: 1e-9);
    }

    /// **Forfeited is a terminal status** — there is no "un-forfeit". The record is of what you
    /// committed to and did, and quietly reopening a lapsed loop would be the exact "make the
    /// uncomfortable one disappear" move the whole Reality Loop is built to prevent.
    [Fact]
    public void A_forfeited_loop_has_no_path_back_to_open()
    {
        // A `Loop` carries a status but exposes no mutator to change it — reconstruction from the
        // event log is the only way its status is ever set.
        var settable = typeof(Loop).GetProperties()
            .Where(p => p.Name == nameof(Loop.Status))
            .Where(p => p.SetMethod?.IsPublic == true)
            .Where(p => p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
                .All(m => m.Name != "IsExternalInit"))
            .ToList();

        Assert.Empty(settable);
    }
}
