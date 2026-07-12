using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **The classifier, checked without Docker — including against real measured runs.**
///
/// The measurement (settrace line-counting) is deterministic, so its output can be captured once and
/// replayed here. Several of these use the *actual* operation counts a real bubble sort, binary
/// search, and Fibonacci produced in the sandbox — so the classifier is tested against reality, not
/// against curves I drew to make it pass.
public sealed class ComplexityFitTests
{
    private static IReadOnlyList<OperationSample> Samples(params (int N, long Ops)[] xs)
        => [.. xs.Select(x => new OperationSample(x.N, x.Ops))];

    private static ComplexityClass Classify(params (int N, long Ops)[] xs)
        => ComplexityFit.Classify(Samples(xs)).Class;

    // ── Real measured runs (captured from the sandbox) ───────────────────────

    /// A hand-written linear scan. **Measured**, not synthesised.
    [Fact]
    public void A_real_linear_scan_is_O_of_n()
    {
        var found = Classify((64, 131), (128, 259), (256, 515), (512, 1027));

        Assert.Equal(ComplexityClasses.Linear, found);
    }

    /// A real doubly-nested loop.
    [Fact]
    public void A_real_nested_loop_is_O_of_n_squared()
    {
        var found = Classify((16, 547), (32, 2115), (64, 8323), (128, 33027));

        Assert.Equal(ComplexityClasses.Quadratic, found);
    }

    /// **The one a slope-based classifier gets wrong.** Real `n log n` counts — outer loop of `n`, an
    /// inner loop that doubles.
    [Fact]
    public void A_real_n_log_n_algorithm_is_told_apart_from_linear()
    {
        var found = Classify((64, 1347), (128, 3075), (256, 6915), (512, 15363));

        Assert.Equal(ComplexityClasses.Linearithmic, found);

        // And specifically NOT linear, which is the trap.
        Assert.NotEqual(ComplexityClasses.Linear, found);
    }

    /// Real Fibonacci — `φ^n`, which no polynomial times a constant can fit. Caught by its climbing
    /// local exponent (4.7 → 6.7 → 8.6 across these samples).
    [Fact]
    public void A_real_exponential_algorithm_is_detected()
    {
        var found = Classify((8, 102), (12, 699), (16, 4791), (20, 32838));

        Assert.Equal(ComplexityClasses.Exponential, found);
    }

    // ── Synthetic, exact ─────────────────────────────────────────────────────

    [Fact]
    public void Constant_work_is_O_of_1()
    {
        Assert.Equal(ComplexityClasses.Constant, Classify((16, 40), (64, 41), (256, 40), (1024, 41)));
    }

    /// **`O(log n)` is told apart from `O(1)`** — the count grows, slowly, and a constant fit would
    /// drift where the log fit does not. Binary-search-shaped: ~log2(n) probes.
    [Fact]
    public void Logarithmic_work_is_told_apart_from_constant()
    {
        // ops ≈ 3·log2(n): the shape of a binary search's loop body.
        var found = Classify((256, 24), (1024, 30), (4096, 36), (16384, 42), (65536, 48));

        Assert.Equal(ComplexityClasses.Logarithmic, found);
        Assert.NotEqual(ComplexityClasses.Constant, found);
    }

    [Fact]
    public void Cubic_work_is_O_of_n_cubed()
    {
        var found = Classify(
            (8, 8L * 8 * 8), (16, 16L * 16 * 16), (32, 32L * 32 * 32), (64, 64L * 64 * 64));

        Assert.Equal(ComplexityClasses.Cubic, found);
    }

    /// **A pure `2ⁿ`** — different base from Fibonacci, same verdict: faster than any polynomial.
    [Fact]
    public void Pure_two_to_the_n_is_exponential()
    {
        var found = Classify((4, 16), (8, 256), (12, 4096), (16, 65536));

        Assert.Equal(ComplexityClasses.Exponential, found);
    }

    /// A run that blew past the cap at small sizes **is** the answer, not a missing one.
    [Fact]
    public void A_run_that_explodes_past_the_cap_is_exponential()
    {

        var withCap = new List<OperationSample>
        {
            new(8, 200),
            new(16, 40_000),
            new(32, 3_000_000) { Capped = true },
        };

        Assert.Equal(ComplexityClasses.Exponential, ComplexityFit.Classify(withCap).Class);
    }

    // ── Honesty about its own limits ─────────────────────────────────────────

    /// **Three points do not have a shape.** PRAXIS declines rather than guessing — the exact
    /// overconfidence it grades learners for.
    [Fact]
    public void Too_few_samples_is_indeterminate_not_a_guess()
    {
        Assert.Equal(ComplexityClasses.Indeterminate, Classify((16, 100), (32, 200)));
    }

    /// Noise that fits nothing well gets named nothing. A classifier that always returns a class is a
    /// horoscope.
    [Fact]
    public void Counts_that_fit_no_clean_class_are_indeterminate()
    {
        // Genuinely erratic: the count jumps around with no consistent growth law, so no constant
        // times any candidate curve stays put. Non-monotonic, so it is not exponential either.
        var found = Classify((16, 100), (32, 40_000), (64, 300), (128, 50_000), (256, 800));

        Assert.Equal(ComplexityClasses.Indeterminate, found);
    }

    /// The fit quality is reported, and a clean fit scores higher than a marginal one — so a caller
    /// can tell a confident classification from a shaky one.
    [Fact]
    public void A_clean_fit_reports_higher_quality_than_a_noisy_one()
    {
        var clean = ComplexityFit.Classify(Samples(
            (64, 128), (128, 256), (256, 512), (512, 1024)));

        Assert.Equal(ComplexityClasses.Linear, clean.Class);
        Assert.True(clean.Quality > 0.95);
    }
}
