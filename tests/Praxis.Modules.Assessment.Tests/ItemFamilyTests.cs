using Praxis.Modules.Assessment.Domain;

namespace Praxis.Modules.Assessment.Tests;

/// **You cannot calibrate an item from one response.**
///
/// PRAXIS generates every item fresh, for one learner, once — which is what makes `APPLY` evidence
/// mean anything (M5.5). It also means every item has been seen by exactly **one person**, and
/// `n = 1` is not a sample. Anyone who claims to have calibrated an item on one response is
/// hand-waving, and the number they produce will look exactly like a real one.
///
/// So the **family** is the calibration unit, and the instance inherits its posterior.
public sealed class ItemFamilyTests
{
    private static ItemFamily Seed(double llm = 0.5, double features = 0.5)
        => ItemFamily.Seed("fam.bm25.saturation", llm, features);

    // ── The prior is a prior, and it is called one ───────────────────────────

    /// `b̂ = 0.6 · LLM + 0.4 · features`. The model gets the larger share because it has *read* the
    /// concept; the feature model is what stops it drifting into whatever it happens to find
    /// impressive that afternoon.
    [Fact]
    public void A_new_family_is_seeded_from_a_model_and_a_feature_count()
    {
        var family = ItemFamily.Seed("f", llmDifficulty: 1.0, featureDifficulty: 0.0);

        Assert.Equal(0.6, family.B, tolerance: 1e-9);
        Assert.Equal(0.6, family.PriorB, tolerance: 1e-9);
    }

    /// **A brand-new family is `Cold`, and PRAXIS says so.** Its difficulty is a guess with a
    /// decimal point, and calling it anything else would be a lie told with statistics.
    [Fact]
    public void A_family_nobody_has_answered_is_cold_and_admits_it()
    {
        var family = Seed();

        Assert.Equal(CalibrationStage.Cold, family.Stage);
        Assert.Equal(0, family.Responses);
    }

    [Fact]
    public void A_prior_with_no_spread_is_a_certainty_and_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemFamily.Seed("f", 0.5, 0.5, priorSpread: 0.0));
    }

    // ── Shrinkage ────────────────────────────────────────────────────────────

    /// **Three responses is not evidence about difficulty. It is an anecdote with error bars.**
    ///
    /// So with `n` tiny, the prior dominates — and it *should*. Nobody has to decide when to switch:
    /// the shrinkage formula does it, continuously, which is why there is no cliff edge where a
    /// family suddenly becomes trustworthy overnight.
    [Fact]
    public void With_almost_no_data_the_prior_holds()
    {
        var family = ItemFamily.Seed("f", llmDifficulty: 0.0, featureDifficulty: 0.0);

        // Three learners all found it much harder than the prior said.
        for (var i = 0; i < 3; i++)
        {
            family.Observe(observedDifficulty: 2.0);
        }

        Assert.True(
            family.B < 1.5,
            $"Three responses moved the difficulty to {family.B:F2}. PRAXIS has believed an anecdote.");
    }

    /// **And with enough data, the data wins.** The prior fades out of the arithmetic entirely.
    [Fact]
    public void With_enough_data_the_evidence_speaks_for_itself()
    {
        var family = ItemFamily.Seed("f", llmDifficulty: 0.0, featureDifficulty: 0.0);

        for (var i = 0; i < 250; i++)
        {
            family.Observe(observedDifficulty: 2.0);
        }

        Assert.True(
            family.B > 1.9,
            $"250 responses said this item is hard, and PRAXIS still says {family.B:F2}. The prior "
            + "is refusing to be corrected by reality.");
    }

    [Fact]
    public void The_stages_advance_with_evidence_and_not_with_time()
    {
        var family = Seed();

        Assert.Equal(CalibrationStage.Cold, family.Stage);

        for (var i = 0; i < ItemFamily.WarmingAt; i++)
        {
            family.Observe(0.5);
        }

        Assert.Equal(CalibrationStage.Warming, family.Stage);

        for (var i = family.Responses; i < ItemFamily.CalibratedAt; i++)
        {
            family.Observe(0.5);
        }

        Assert.Equal(CalibrationStage.Calibrated, family.Stage);
    }

    // ── We do not withhold a learner's evidence while we wait for statistics ──

    /// **The honest thing PRAXIS does while it is still unsure.**
    ///
    /// A learner does real work on Tuesday. The family it came from has eleven responses, so PRAXIS
    /// genuinely does not know how hard the item was.
    ///
    /// The cowardly option is to withhold the evidence until the statistics arrive — which means
    /// pretending the learner's Tuesday did not happen. PRAXIS **counts it immediately and weakly**
    /// (κ × 0.6), and re-weights it retroactively on the nightly rebuild. The event log makes that
    /// trivially correct: the Twin is a *projection*, so it can simply be recomputed with better
    /// numbers.
    ///
    /// > We do not withhold learning signal while we wait for statistics. We discount it, and we come
    /// > back and fix it.
    [Fact]
    public void Evidence_from_an_uncalibrated_family_counts_immediately_but_weakly()
    {
        var family = Seed();

        Assert.Equal(0.6, family.UpdateGainMultiplier, tolerance: 1e-9);

        Assert.True(
            family.UpdateGainMultiplier > 0,
            "A learner's work is being thrown away because our statistics are not ready. They did "
            + "the work. The uncertainty about how hard it was is OUR problem, not theirs.");

        Assert.True(family.UpdateGainMultiplier < 1.0);
    }

    [Fact]
    public void A_calibrated_family_counts_in_full()
    {
        var family = Seed();

        for (var i = 0; i < ItemFamily.CalibratedAt; i++)
        {
            family.Observe(0.5);
        }

        Assert.Equal(1.0, family.UpdateGainMultiplier, tolerance: 1e-9);
    }

    // ── A family with a large spread is not a family. It is a bag. ───────────

    /// **The sentence to remember.**
    ///
    /// If instances of a "family" vary wildly in how hard they are, then the thing binding them
    /// together was never a deep structure — it was a **folder name**, and every difficulty estimate
    /// inherited from it is noise wearing a decimal point.
    [Fact]
    public void A_family_whose_instances_disagree_wildly_is_not_a_family()
    {
        var bag = ItemFamily.Seed("looks-like-a-family", 0.0, 0.0);

        // Instances scattered all over the scale. Nothing binds them but the name.
        var scatter = new[] { -2.5, 2.5, -2.0, 3.0, -3.0, 2.0 };

        for (var i = 0; i < 60; i++)
        {
            bag.Observe(scatter[i % scatter.Length]);
        }

        Assert.True(
            bag.IsABag,
            $"A family whose instances scatter across five logits (spread {bag.ObservedSpread:F2}) "
            + "is being treated as a calibrated family. Every item drawn from it inherits a "
            + "difficulty that means nothing.");
    }

    [Fact]
    public void A_real_family_is_not_flagged_as_a_bag()
    {
        var family = ItemFamily.Seed("real", 0.5, 0.5);

        var rng = new Random(3);

        for (var i = 0; i < 60; i++)
        {
            // Instances of a genuine family vary a little. That is what a family *is*.
            family.Observe(0.5 + ((rng.NextDouble() - 0.5) * 0.4));
        }

        Assert.False(family.IsABag, $"A tight family was called a bag (spread {family.ObservedSpread:F2}).");
    }

    /// A bag cannot be diagnosed from three responses either — the spread has to have had a chance
    /// to show itself.
    [Fact]
    public void A_family_is_not_condemned_before_there_is_evidence_to_condemn_it()
    {
        var family = Seed();

        family.Observe(-3.0);
        family.Observe(3.0);

        Assert.False(family.IsABag, "Two responses were enough to condemn a family.");
    }
}
