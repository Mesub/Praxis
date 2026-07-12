using Praxis.Modules.Twin.Learning;

namespace Praxis.Modules.Twin.Tests;

/// The belief update. Everything PRAXIS ever says about a learner starts here, so it is worth being
/// exact about what it does and — more importantly — what it refuses to do.
public sealed class BktTests
{
    private static readonly BktParameters Typical = BktParameters.Create(
        transit: 0.20, slip: 0.10, guess: 0.15);

    [Fact]
    public void A_correct_answer_raises_belief()
    {
        var after = Bkt.Observe(0.30, correct: true, Typical);

        Assert.True(after > 0.30, $"Belief fell to {after} after a correct answer.");
    }

    [Fact]
    public void A_wrong_answer_lowers_belief()
    {
        var after = Bkt.Observe(0.70, correct: false, Typical);

        Assert.True(after < 0.70, $"Belief rose to {after} after a wrong answer.");
    }

    /// **The classic BKT bug**, and the reason `Observe` separates inference from learning.
    ///
    /// If the learning step is applied before the evidence, a wrong answer can still *raise*
    /// belief — the model reasons "they had an opportunity to practise, so they probably learned
    /// something" and quietly ignores that they got it wrong. The Twin then congratulates a learner
    /// for failing.
    [Fact]
    public void A_wrong_answer_never_raises_belief_no_matter_the_parameters()
    {
        var priors = new[] { 0.05, 0.2, 0.5, 0.8, 0.95 };
        var transits = new[] { 0.05, 0.2, 0.4, 0.6 };
        var slips = new[] { 0.02, 0.1, 0.2, 0.3 };
        var guesses = new[] { 0.02, 0.1, 0.25, 0.4 };

        var checkedCount = 0;

        foreach (var prior in priors)
        {
            foreach (var transit in transits)
            {
                foreach (var slip in slips)
                {
                    foreach (var guess in guesses)
                    {
                        if (slip + guess >= BktParameters.MaxSlipPlusGuess)
                        {
                            continue;
                        }

                        var p = BktParameters.Create(transit, slip, guess);

                        var wrong = Bkt.Observe(prior, correct: false, p);
                        var right = Bkt.Observe(prior, correct: true, p);

                        checkedCount++;

                        // Getting it wrong must never leave you better off than getting it right.
                        Assert.True(
                            wrong < right,
                            $"With prior={prior}, T={transit}, S={slip}, G={guess}: a wrong answer "
                            + $"({wrong:F4}) was worth at least as much as a right one ({right:F4}). "
                            + "The Twin is congratulating a learner for failing.");
                    }
                }
            }
        }

        Assert.True(checkedCount > 100, "The sweep did not actually run.");
    }

    [Fact]
    public void A_lucky_guess_is_worth_less_than_a_confident_solve()
    {
        // The same correct answer means less on an item you could have guessed.
        var guessable = BktParameters.Create(transit: 0.2, slip: 0.1, guess: 0.40);
        var unguessable = BktParameters.Create(transit: 0.2, slip: 0.1, guess: 0.02);

        var fromGuessable = Bkt.Observe(0.30, correct: true, guessable);
        var fromUnguessable = Bkt.Observe(0.30, correct: true, unguessable);

        Assert.True(
            fromUnguessable > fromGuessable,
            "A correct answer on an item nobody could guess should move belief more than one on a "
            + "four-option multiple choice. This is why BUILD outweighs RECALL.");
    }

    [Fact]
    public void One_answer_never_settles_anything()
    {
        // Slip and guess are why a single response is weak evidence. Even a perfect answer from a
        // learner we believed knew nothing leaves real doubt.
        var after = Bkt.Observe(ConceptStateStart, correct: true, Typical);

        Assert.True(after < 0.75, $"One correct answer took belief to {after:F2}. That is not proof.");
    }

    [Fact]
    public void Repeated_success_converges_towards_but_never_reaches_certainty()
    {
        var p = ConceptStateStart;

        for (var i = 0; i < 50; i++)
        {
            p = Bkt.Observe(p, correct: true, Typical);
        }

        Assert.True(p > 0.95, $"Fifty correct answers only reached {p:F3}.");
        Assert.True(p < 1.0, "Belief reached certainty. Nothing about a human is ever certain.");
    }

    [Fact]
    public void A_single_failure_after_a_long_streak_dents_belief_without_erasing_it()
    {
        // One bad day is not amnesia. But it is not nothing, either.
        var p = ConceptStateStart;

        for (var i = 0; i < 20; i++)
        {
            p = Bkt.Observe(p, correct: true, Typical);
        }

        var before = p;
        var after = Bkt.Observe(p, correct: false, Typical);

        Assert.True(after < before);
        Assert.True(after > 0.5, $"A single slip collapsed belief from {before:F2} to {after:F2}.");
    }

    /// **The absorbing-state trap**, and the reason belief is clamped strictly inside (0, 1).
    ///
    /// The transit step drives belief asymptotically to 1, and in floating point it *arrives*. At
    /// exactly 1.0 the Bayesian update dies — the "did not know it" branch is weighted by (1 − P) =
    /// 0, so nothing can ever move belief again. The Twin would insist a learner knows something
    /// while watching them fail, forever, and no evidence could correct it.
    ///
    /// This is not numerical hygiene. An unfalsifiable belief about a person is not a model, it is
    /// a verdict.
    [Fact]
    public void Belief_never_becomes_unfalsifiable()
    {
        var p = 0.10;

        // Enough success to saturate a naive implementation to exactly 1.0.
        for (var i = 0; i < 200; i++)
        {
            p = Bkt.Observe(p, correct: true, Typical);
        }

        Assert.True(p < 1.0, "Belief reached certainty, and can now never be revised downwards.");
        Assert.True(p >= Bkt.MaxBelief - 1e-9);

        // And from there, failure still bites.
        var afterFailure = Bkt.Observe(p, correct: false, Typical);
        Assert.True(afterFailure < p, "At the ceiling, a wrong answer changed nothing. The Twin is unfalsifiable.");

        // The mirror trap: certainty that a learner knows nothing would make learning unprovable.
        var hopeless = 0.10;
        for (var i = 0; i < 200; i++)
        {
            hopeless = Bkt.Observe(hopeless, correct: false, Typical);
        }

        Assert.True(hopeless > 0.0, "Belief reached zero, and can now never be raised.");

        var afterSuccess = Bkt.Observe(hopeless, correct: true, Typical);
        Assert.True(afterSuccess > hopeless, "At the floor, a correct answer changed nothing.");
    }

    [Fact]
    public void Sustained_failure_collapses_belief()
    {
        var p = 0.90;

        for (var i = 0; i < 8; i++)
        {
            p = Bkt.Observe(p, correct: false, Typical);
        }

        Assert.True(p < 0.25, $"Eight consecutive failures still left belief at {p:F2}.");
    }

    /// **The degenerate regime**, refused at construction.
    ///
    /// When slip + guess ≥ 1, the model inverts: a correct answer becomes evidence *against*
    /// knowing, because a knower is likelier to slip than a guesser is to guess. The Twin would be
    /// confidently backwards — and PRAXIS would teach hardest to the people getting things right.
    ///
    /// This is a real, documented failure mode of fitted BKT. We refuse it rather than hope.
    [Fact]
    public void Degenerate_parameters_are_refused()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => BktParameters.Create(transit: 0.2, slip: 0.6, guess: 0.5));

        Assert.Contains("degenerate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("teach hardest to the people getting things right", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_parameters_seeded_from_difficulty_are_never_degenerate()
    {
        // The seed is what every concept in every pack starts from, so it must be safe across the
        // entire difficulty range — including the values a careless author might write.
        for (var d = -4.0; d <= 4.0; d += 0.1)
        {
            var p = BktParameters.ForDifficulty(d);

            Assert.True(
                p.Slip + p.Guess < BktParameters.MaxSlipPlusGuess,
                $"Difficulty {d:F1} seeds a degenerate model.");
        }
    }

    [Fact]
    public void Harder_concepts_slip_more_and_are_guessed_less()
    {
        var easy = BktParameters.ForDifficulty(-2.0);     // tokenisation
        var hard = BktParameters.ForDifficulty(1.5);      // beating the baseline

        // You can guess a definition. You cannot guess a working retrieval pipeline.
        Assert.True(hard.Guess < easy.Guess);

        // A shaky grasp fails under load in ways a solid one does not.
        Assert.True(hard.Slip > easy.Slip);

        // One good session teaches you tokenisation. It does not teach you to beat BM25.
        Assert.True(hard.Transit < easy.Transit);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void The_parameters_are_probabilities(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BktParameters.Create(bad, 0.1, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BktParameters.Create(0.2, bad, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BktParameters.Create(0.2, 0.1, bad));
    }

    [Fact]
    public void The_update_is_pure_and_replaying_gives_the_same_answer()
    {
        // The whole architecture rests on this. If the Twin's update read a clock or a random, a
        // rebuild of the same log would produce a different learner — and the Ledger would be
        // unverifiable.
        var first = Sequence();
        var second = Sequence();

        Assert.Equal(first, second);

        static List<double> Sequence()
        {
            var p = ConceptStateStart;
            var trace = new List<double>();

            foreach (var correct in new[] { true, false, true, true, false, true, true, true })
            {
                p = Bkt.Observe(p, correct, Typical);
                trace.Add(p);
            }

            return trace;
        }
    }

    /// The prior a learner starts from: PRAXIS believes you know nothing until you show otherwise.
    private const double ConceptStateStart = 0.10;
}
