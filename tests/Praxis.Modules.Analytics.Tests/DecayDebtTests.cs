using Praxis.Modules.Analytics.Domain;

namespace Praxis.Modules.Analytics.Tests;

/// **How much of what you proved has quietly slipped, and is owed back to review.** Decay debt splits
/// each held competency's credit into what is still retrievable and what has bled away — and the two
/// always sum to the whole, so the debt is never a scary number invented on the side.
public sealed class DecayDebtTests
{
    private static ConceptDecay Held(double credit, double retrievability)
    {
        // Mirror the Twin's own thresholds: decaying is [0.70, 0.90), lapsed is below 0.70.
        var hasLapsed = retrievability < 0.70;
        var isDecaying = retrievability < 0.90 && retrievability >= 0.70;
        return new ConceptDecay(credit, retrievability, isDecaying, hasLapsed);
    }

    [Fact]
    public void A_learner_who_has_proved_nothing_owes_nothing()
    {
        var report = DecayDebt.From([]);

        Assert.False(report.HasHeldConcepts);
        Assert.False(report.HasDebt);
        Assert.Equal(0.0, report.CreditAtRisk);
    }

    [Fact]
    public void A_freshly_proved_concept_is_fully_retained_and_owes_nothing()
    {
        var report = DecayDebt.From([Held(credit: 0.8, retrievability: 1.0)]);

        Assert.True(report.HasHeldConcepts);
        Assert.False(report.HasDebt);
        Assert.Equal(0.0, report.CreditAtRisk, 6);
        Assert.Equal(0.8, report.RetainedCredit, 6);
    }

    [Fact]
    public void A_decaying_concept_puts_its_forgotten_share_at_risk()
    {
        // Retrievability 0.80 → 20% of the 0.5 credit has bled away.
        var report = DecayDebt.From([Held(credit: 0.5, retrievability: 0.80)]);

        Assert.Equal(0.10, report.CreditAtRisk, 6);
        Assert.Equal(0.40, report.RetainedCredit, 6);
        Assert.Equal(1, report.ConceptsDecaying);
        Assert.Equal(0, report.ConceptsLapsed);
        Assert.True(report.HasDebt);
    }

    [Fact]
    public void A_lapsed_concept_is_counted_lapsed_not_merely_decaying()
    {
        var report = DecayDebt.From([Held(credit: 0.9, retrievability: 0.50)]);

        Assert.Equal(0, report.ConceptsDecaying);
        Assert.Equal(1, report.ConceptsLapsed);
    }

    [Fact]
    public void The_debt_and_what_is_retained_always_sum_to_the_whole_held_credit()
    {
        var report = DecayDebt.From(
        [
            Held(credit: 1.0, retrievability: 0.95),   // fresh
            Held(credit: 0.6, retrievability: 0.80),   // decaying
            Held(credit: 0.4, retrievability: 0.40),   // lapsed
        ]);

        var totalHeld = 1.0 + 0.6 + 0.4;
        Assert.Equal(totalHeld, report.CreditAtRisk + report.RetainedCredit, 6);
        Assert.Equal(1, report.ConceptsDecaying);
        Assert.Equal(1, report.ConceptsLapsed);
    }

    [Fact]
    public void Retrievability_is_clamped_so_a_stray_value_cannot_manufacture_negative_debt()
    {
        // A retrievability above 1.0 must not turn into negative "at risk" credit.
        var report = DecayDebt.From([new ConceptDecay(0.5, Retrievability: 1.2, IsDecaying: false, HasLapsed: false)]);

        Assert.Equal(0.0, report.CreditAtRisk, 6);
        Assert.Equal(0.5, report.RetainedCredit, 6);
    }
}
