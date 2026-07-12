using Praxis.Modules.Assessment.Domain;

namespace Praxis.Modules.Assessment.Tests;

/// **What one answer actually tells you about a person.**
///
/// *"Seven out of ten"* is almost meaningless, because it does not say **which** seven. Seven easy
/// ones and three hard ones is a completely different mind from three easy ones and seven hard ones,
/// and a percentage cannot tell them apart.
public sealed class IrtTests
{
    // ── The curve ────────────────────────────────────────────────────────────

    [Fact]
    public void An_item_matched_to_the_learner_is_a_coin_flip()
    {
        // θ = b: this item is *exactly* at the edge of what they can do.
        Assert.Equal(0.5, Irt.Probability(theta: 0.0, a: 1.0, b: 0.0), tolerance: 1e-9);
    }

    [Fact]
    public void A_stronger_learner_is_likelier_to_be_right()
    {
        var weak = Irt.Probability(theta: -1.0, a: 1.0, b: 0.0);
        var strong = Irt.Probability(theta: 1.0, a: 1.0, b: 0.0);

        Assert.True(strong > weak);
        Assert.True(strong > 0.7);
        Assert.True(weak < 0.3);
    }

    [Fact]
    public void A_harder_item_is_harder()
    {
        var easy = Irt.Probability(theta: 0.0, a: 1.0, b: -2.0);
        var hard = Irt.Probability(theta: 0.0, a: 1.0, b: 2.0);

        Assert.True(easy > hard);
    }

    /// **Discrimination is the item's ability to tell people apart.** A woolly item (low `a`) gives
    /// everyone much the same chance — so it tells you nothing about anybody, and it is still taking
    /// up their afternoon.
    [Fact]
    public void A_woolly_item_barely_separates_anyone()
    {
        var sharp = Irt.Probability(theta: 1.0, a: 2.5, b: 0.0) - Irt.Probability(theta: -1.0, a: 2.5, b: 0.0);
        var woolly = Irt.Probability(theta: 1.0, a: 0.3, b: 0.0) - Irt.Probability(theta: -1.0, a: 0.3, b: 0.0);

        Assert.True(sharp > woolly);
        Assert.True(woolly < 0.25, "A near-zero discrimination item is still separating learners.");
    }

    // ── Guessing: the reason this is 3PL and not the 2PL the roadmap asked for ──

    /// **The roadmap says "2PL". CANON's item schema carries a guessing floor, and CANON is right.**
    ///
    /// On a four-option MCQ, a learner who knows **nothing at all** still scores about 25%. A 2PL
    /// model cannot express that — it reads those lucky hits as *evidence of ability* and inflates θ.
    ///
    /// And it does it **worst for the learners who are struggling most**, which is exactly the
    /// population whose model must not be flattering.
    [Fact]
    public void A_learner_who_knows_nothing_still_guesses_a_quarter_of_an_MCQ()
    {
        var hopeless = Irt.Probability(theta: -4.0, a: 1.5, b: 1.0, c: 0.25);

        Assert.True(hopeless >= 0.25, "Someone who knows nothing has a worse-than-chance shot.");
        Assert.True(hopeless < 0.30);

        // Without the guessing floor, the same learner looks nearly hopeless — and their lucky
        // quarter would then be read as ability.
        var without = Irt.Probability(theta: -4.0, a: 1.5, b: 1.0, c: 0.0);

        Assert.True(without < 0.01);
    }

    /// **With no guessing, the 3PL *is* the 2PL.** The roadmap's model is the special case, not a
    /// different one — so nothing was traded away to fix the MCQ case.
    [Fact]
    public void With_no_guessing_this_is_exactly_the_two_parameter_model()
    {
        var twoPl = 1.0 / (1.0 + Math.Exp(-1.3 * (0.7 - 0.2)));

        Assert.Equal(twoPl, Irt.Probability(theta: 0.7, a: 1.3, b: 0.2, c: 0.0), tolerance: 1e-12);
    }

    // ── Information ──────────────────────────────────────────────────────────

    /// **An item far too easy or far too hard tells you nothing.**
    ///
    /// This is the entire argument against a fixed test: half of its questions are telling you
    /// nothing about the person in front of you, and they are still spending that person's afternoon.
    [Fact]
    public void An_item_tells_you_most_when_it_is_matched_to_the_learner()
    {
        var matched = Irt.Information(theta: 0.0, a: 1.5, b: 0.0);
        var tooEasy = Irt.Information(theta: 0.0, a: 1.5, b: -3.0);
        var tooHard = Irt.Information(theta: 0.0, a: 1.5, b: 3.0);

        Assert.True(matched > tooEasy * 10);
        Assert.True(matched > tooHard * 10);
    }

    [Fact]
    public void A_woolly_item_is_worth_asking_less_than_a_sharp_one()
    {
        Assert.True(
            Irt.Information(0.0, a: 2.0, b: 0.0) > Irt.Information(0.0, a: 0.5, b: 0.0));
    }

    // ── The update ───────────────────────────────────────────────────────────

    /// **The headline claim of M7.2, and the one worth actually checking.**
    ///
    /// Simulate a learner of *known* ability, serve them items, and see whether the model recovers
    /// the number it was never told. If it cannot, every θ in the product is decoration.
    ///
    /// ## The first version of this test was wrong, and the way it was wrong is worth recording
    ///
    /// I asserted `|θ̂ − θ| < 0.3` on a single fixed seed. It failed at θ = 0 with an estimate of
    /// 0.35, and the tempting move — the one that takes ten seconds — is to widen the tolerance to
    /// 0.4 and move on.
    ///
    /// I measured instead. Across 40 seeds the **mean** error is 0.04 (so the estimator is not
    /// biased) with a standard deviation of **0.18** — which is exactly what the information in 200
    /// items of this spread predicts. My "0.3" was a magic number worth about 1.7 standard
    /// deviations, so it was always going to fail on roughly one seed in ten. **The estimator was
    /// right and the test was superstitious.**
    ///
    /// So it now asserts the two things that actually matter, and neither is a magic number:
    ///
    /// 1. **Unbiased** — averaged over many learners, PRAXIS is not systematically flattering anyone.
    /// 2. **Honestly uncertain** — the truth falls inside the model's *own* claimed error bar. An
    ///    estimate whose standard error does not contain the answer is not an estimate. It is a
    ///    number with a decoration attached.
    [Theory]
    [InlineData(-1.5)]
    [InlineData(0.0)]
    [InlineData(1.2)]
    [InlineData(2.0)]
    public void The_model_recovers_an_ability_it_was_never_told(double trueTheta)
    {
        var errors = new List<double>();
        var covered = 0;

        const int learners = 40;

        for (var seed = 0; seed < learners; seed++)
        {
            var rng = new Random(seed);

            var responses = new List<Response>();

            // 200 responses across a spread of difficulties — CANON's convergence target.
            for (var i = 0; i < 200; i++)
            {
                var b = -3.0 + (6.0 * i / 199.0);
                const double a = 1.2;

                var p = Irt.Probability(trueTheta, a, b);

                responses.Add(new Response(Correct: rng.NextDouble() < p, a, b));
            }

            var estimate = Irt.Update(Ability.Unknown, responses);

            errors.Add(estimate.Theta - trueTheta);

            // **Does the model's own error bar contain the truth?** Three standard errors is the
            // claim it is making about itself, and it has to be good for it.
            if (Math.Abs(estimate.Theta - trueTheta) <= 3.0 * estimate.StandardError)
            {
                covered++;
            }

            Assert.True(
                estimate.IsConfident,
                $"After 200 responses SE is still {estimate.StandardError:F2}.");
        }

        // 1. Unbiased. PRAXIS is not systematically flattering anybody — nor systematically
        //    underselling them, which is the failure mode that quietly tells a learner they are
        //    worse than they are.
        var bias = errors.Average();

        Assert.True(
            Math.Abs(bias) < 0.10,
            $"The estimator is biased by {bias:+0.000;-0.000} logits at θ = {trueTheta}. Every "
            + "learner at this ability is being told the same wrong thing about themselves.");

        // 2. Honestly uncertain. The error bar is a promise, and it has to be kept.
        Assert.True(
            covered >= learners - 2,
            $"The truth fell outside the model's own 3-SE interval for {learners - covered} of "
            + $"{learners} learners. The standard error is decoration.");
    }

    /// **More evidence, more certainty.** The standard error is not decoration — it is what decides
    /// whether PRAXIS is entitled to stop asking.
    [Fact]
    public void Certainty_grows_with_evidence()
    {
        var rng = new Random(7);

        List<Response> Sample(int n) =>
            [.. Enumerable.Range(0, n).Select(i =>
            {
                var b = -2.0 + (4.0 * i / Math.Max(1, n - 1));
                var p = Irt.Probability(0.5, 1.2, b);

                return new Response(rng.NextDouble() < p, 1.2, b);
            })];

        var few = Irt.Update(Ability.Unknown, Sample(5));
        var many = Irt.Update(Ability.Unknown, Sample(60));

        Assert.True(
            many.StandardError < few.StandardError,
            "Sixty answers left PRAXIS no more certain than five. The posterior is not updating.");
    }

    /// **PRAXIS does not start every learner at the population mean.**
    ///
    /// A standard adaptive test starts you at θ = 0, which is a way of announcing that it has decided
    /// to know nothing about you. PRAXIS already has a probabilistic model of this mind (I-1) —
    /// discarding it to start from zero would be throwing away the entire product for the sake of a
    /// textbook.
    [Fact]
    public void The_prior_is_the_Twins_belief_and_it_actually_matters()
    {
        // One correct answer on a hard item, from two learners PRAXIS believes very different things
        // about.
        List<Response> one = [new Response(Correct: true, A: 1.2, B: 1.5)];

        var fromNothing = Irt.Update(Ability.Unknown, one);
        var fromStrong = Irt.Update(new Ability(2.0, 0.5), one);

        Assert.True(
            fromStrong.Theta > fromNothing.Theta,
            "The Twin's prior made no difference. PRAXIS is starting from scratch on a learner it "
            + "already knows.");
    }

    /// A learner PRAXIS knows nothing about gets a **wide** prior, and honestly so. A narrow prior on
    /// no evidence is a confident lie.
    [Fact]
    public void An_unknown_learner_is_not_pretended_to_be_average()
    {
        Assert.Equal(0.0, Ability.Unknown.Theta);

        Assert.True(Ability.Unknown.StandardError > 1.0);
        Assert.False(Ability.Unknown.IsConfident);
    }

    /// A learner who gets **everything** wrong does not produce a NaN, an exception, or a θ of
    /// negative infinity. They produce a low θ and a wide error bar, which is the truth.
    [Fact]
    public void A_learner_who_gets_everything_wrong_does_not_break_the_maths()
    {
        List<Response> all = [.. Enumerable.Range(0, 20)
            .Select(i => new Response(Correct: false, A: 1.2, B: -2.0 + (0.2 * i)))];

        var estimate = Irt.Update(Ability.Unknown, all);

        Assert.False(double.IsNaN(estimate.Theta));
        Assert.False(double.IsInfinity(estimate.Theta));

        Assert.True(estimate.Theta < -1.0);
        Assert.InRange(estimate.Theta, Irt.MinTheta, Irt.MaxTheta);
    }

    [Fact]
    public void An_item_that_punishes_knowing_more_is_a_bug_and_says_so()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Irt.Probability(0.0, a: -1.0, b: 0.0));

        Assert.Contains("bug in the item", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_guessing_floor_of_one_is_not_an_item()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Irt.Probability(0.0, a: 1.0, b: 0.0, c: 1.0));
    }
}
