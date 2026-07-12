namespace Praxis.Modules.Reality.Domain;

/// **Fitting a growth class to what the code actually did.**
///
/// The learner predicts the time complexity of a program; PRAXIS runs it at growing input sizes,
/// counts the Python-level operations at each (deterministically — see `ComplexityHarness`), and this
/// class decides which curve those counts lie on.
///
/// ## Why constant-ratio, not a slope
///
/// The obvious method is to fit a line through `log(ops)` vs `log(n)` and read the slope — 1 is
/// linear, 2 is quadratic. It works for pure polynomials and **fails on the two classes that matter
/// most in a data-structures course**: `O(log n)` and `O(n log n)`, whose log-log slopes are not
/// clean integers and blur into their neighbours.
///
/// So instead: for each candidate curve `f`, form the ratio `ops(n) / f(n)` at every sample. If `f`
/// is the right class, that ratio is **constant** (it is the hidden constant factor); if `f` is
/// wrong, the ratio drifts. The class whose ratio drifts *least* — lowest coefficient of variation —
/// wins. This separates `O(n)` from `O(n log n)` cleanly, because dividing `n log n` counts by `n`
/// leaves a rising `log n`, whose drift the metric sees.
///
/// ## Exponential is detected, not fitted
///
/// No constant times a polynomial fits `φ^n`, so exponential code is caught by a different tell: its
/// **local growth exponent keeps climbing**. Between two samples, `log(ops₂/ops₁) / log(n₂/n₁)` is
/// roughly the degree — constant for a polynomial, steeper and steeper for an exponential. Past
/// cubic and rising, or the operation count simply exploding past the cap, it is exponential. This is
/// honest about its own limit: PRAXIS does not claim to tell `2ⁿ` from `n!` from `φ^n` — it says
/// *"faster than any polynomial"*, because at the sizes a sandbox can run, that is all the data can
/// truthfully support.
public static class ComplexityFit
{
    /// Below this many usable samples, PRAXIS will not name a class — three points do not have a
    /// shape, and guessing one would be the tool doing the exact overconfidence it grades learners
    /// for.
    public const int MinimumSamples = 3;

    /// A local growth exponent steeper than this — steeper than `n³` — and still climbing is the
    /// signature of super-polynomial growth.
    public const double SuperPolynomialExponent = 3.5;

    /// Classify a run. Returns the class the counts best fit, and how good the fit was.
    public static ComplexityFinding Classify(IReadOnlyList<OperationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var usable = samples
            .Where(s => s.Operations is > 0 && !s.Capped)
            .OrderBy(s => s.N)
            .ToList();

        var capped = samples.Any(s => s.Capped);

        if (usable.Count < MinimumSamples)
        {
            // Not enough sub-cap points to fit — **unless the run exploded**, which is itself the
            // answer. A run that blew past the operation cap at small sizes did not fail to produce
            // data; it produced the loudest possible signal.
            return capped && usable.Count >= 2 && IsExploding(usable)
                ? new ComplexityFinding(ComplexityClasses.Exponential, Quality: 1.0)
                : new ComplexityFinding(ComplexityClasses.Indeterminate, Quality: 0.0);
        }

        if (capped && IsExploding(usable))
        {
            return new ComplexityFinding(ComplexityClasses.Exponential, Quality: 1.0);
        }

        // Drop the two smallest samples when we can afford to: at tiny n a constant per-call overhead
        // (function setup, loop priming) is a big fraction of the count and drags the fit toward a
        // higher class than the true asymptotic one.
        var forFit = usable.Count >= MinimumSamples + 2 ? usable.Skip(2).ToList() : usable;

        if (IsExploding(forFit))
        {
            return new ComplexityFinding(ComplexityClasses.Exponential, Quality: 1.0);
        }

        var best = ComplexityClasses.Polynomialish
            .Select(c => (Class: c, Cv: CoefficientOfVariation(forFit, c)))
            .OrderBy(x => x.Cv)
            .First();

        // A "best" fit that still drifts wildly is not a fit. Better to say *indeterminate* than to
        // name a class the data does not support.
        return best.Cv > 0.35
            ? new ComplexityFinding(ComplexityClasses.Indeterminate, Quality: 0.0)
            : new ComplexityFinding(best.Class, Quality: 1.0 - best.Cv);
    }

    /// The local growth exponent between the last two usable samples, and whether it exceeds cubic.
    /// For a polynomial this settles to the degree; for an exponential it keeps climbing.
    private static bool IsExploding(List<OperationSample> samples)
    {
        if (samples.Count < 2)
        {
            return false;
        }

        var a = samples[^2];
        var b = samples[^1];

        if (a.Operations <= 0 || b.Operations <= 0 || b.N <= a.N)
        {
            return false;
        }

        var localExponent =
            Math.Log((double)b.Operations / a.Operations) / Math.Log((double)b.N / a.N);

        return localExponent > SuperPolynomialExponent;
    }

    /// How constant `ops(n) / f(n)` is across the samples. Zero means a perfect fit; the class with
    /// the smallest value is the one the data lies on.
    private static double CoefficientOfVariation(List<OperationSample> samples, ComplexityClass c)
    {
        var ratios = samples.Select(s => s.Operations / c.Growth(s.N)).ToList();

        var mean = ratios.Average();

        if (mean <= 0)
        {
            return double.MaxValue;
        }

        var variance = ratios.Select(r => (r - mean) * (r - mean)).Average();

        return Math.Sqrt(variance) / mean;
    }
}

/// One measurement: how many operations the code took at input size `n`.
public sealed record OperationSample(int N, long Operations)
{
    /// The count hit the operation cap and was cut off. **A signal, not a failure** — it means the
    /// code grows explosively, which is exactly what the exponential detector is looking for.
    public bool Capped { get; init; }
}

/// The verdict of a fit.
public sealed record ComplexityFinding(ComplexityClass Class, double Quality);

/// A growth class, with the curve PRAXIS fits against it.
public sealed record ComplexityClass(string Label, Func<int, double> Growth)
{
    public override string ToString() => Label;
}

public static class ComplexityClasses
{
    public static readonly ComplexityClass Constant = new("O(1)", _ => 1.0);
    public static readonly ComplexityClass Logarithmic = new("O(log n)", n => Math.Log2(Math.Max(2, n)));
    public static readonly ComplexityClass Linear = new("O(n)", n => n);
    public static readonly ComplexityClass Linearithmic = new("O(n log n)", n => n * Math.Log2(Math.Max(2, n)));
    public static readonly ComplexityClass Quadratic = new("O(n^2)", n => (double)n * n);
    public static readonly ComplexityClass Cubic = new("O(n^3)", n => (double)n * n * n);

    /// **Named, not fitted** — a bucket for anything faster than a polynomial (see `ComplexityFit`).
    public static readonly ComplexityClass Exponential = new("O(2^n) or worse", n => Math.Pow(2, n));

    /// **PRAXIS declines to name a class.** The data did not support one, and a diagnostic that hides
    /// its own uncertainty is doing the thing it accuses the learner of.
    public static readonly ComplexityClass Indeterminate = new("indeterminate", _ => 1.0);

    /// The classes the constant-ratio fit chooses between. Exponential is detected separately, and
    /// Indeterminate is never chosen — it is what is *returned* when nothing fits.
    public static readonly IReadOnlyList<ComplexityClass> Polynomialish =
    [
        Constant, Logarithmic, Linear, Linearithmic, Quadratic, Cubic,
    ];
}
