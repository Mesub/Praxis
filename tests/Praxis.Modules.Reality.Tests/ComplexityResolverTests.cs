using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Tests;

/// **Complexity prediction: the learner commits a class, and reality measures the scaling.**
public sealed class ComplexityResolverTests
{
    // ── Parsing what a learner might type ────────────────────────────────────

    [Theory]
    [InlineData("O(n)", "O(n)")]
    [InlineData("linear", "O(n)")]
    [InlineData("O(n^2)", "O(n^2)")]
    [InlineData("O(n**2)", "O(n^2)")]
    [InlineData("quadratic", "O(n^2)")]
    [InlineData("O(n log n)", "O(n log n)")]
    [InlineData("n log n", "O(n log n)")]
    [InlineData("linearithmic", "O(n log n)")]
    [InlineData("O(log n)", "O(log n)")]
    [InlineData("O(1)", "O(1)")]
    [InlineData("constant", "O(1)")]
    [InlineData("O(n^3)", "O(n^3)")]
    [InlineData("exponential", "O(2^n) or worse")]
    [InlineData("O(2^n)", "O(2^n) or worse")]
    [InlineData("O(n!)", "O(2^n) or worse")]
    [InlineData("Θ(n)", "O(n)")]
    public void The_many_ways_of_writing_a_class_are_understood(string input, string label)
    {
        Assert.Equal(label, ComplexityAnswers.Parse(input)!.Label);
    }

    /// **`n log n` must not be read as `n` or as `log n`** — it contains both substrings, and a naive
    /// parser matches whichever it checks first. This is the ordering bug the parser is written to
    /// avoid, so it gets its own test.
    [Fact]
    public void N_log_n_is_not_mistaken_for_linear_or_logarithmic()
    {
        Assert.Equal(ComplexityClasses.Linearithmic, ComplexityAnswers.Parse("O(n log n)"));
        Assert.NotEqual(ComplexityClasses.Linear, ComplexityAnswers.Parse("O(n log n)"));
        Assert.NotEqual(ComplexityClasses.Logarithmic, ComplexityAnswers.Parse("O(n log n)"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("it's pretty fast")]
    [InlineData("banana")]
    public void An_unrecognised_answer_is_null(string input)
    {
        Assert.Null(ComplexityAnswers.Parse(input));
    }

    // ── Commit discipline ────────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void A_complexity_prediction_still_needs_a_valid_confidence(double confidence)
    {
        Assert.False(ComplexityResolver.Commit("O(n)", confidence).IsSuccess);
    }

    [Fact]
    public void A_commitment_that_does_not_name_a_class_is_refused()
    {
        var committed = ComplexityResolver.Commit("dunno, fastish", 0.8);

        Assert.False(committed.IsSuccess);
        Assert.Contains("did not recognise", committed.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_committed_complexity_cannot_be_mutated_after_the_fact()
    {
        var writable = typeof(CommittedComplexity)
            .GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true)
            .ToList();

        // Records have init-only setters; none should be a plain public setter.
        Assert.All(writable, p => Assert.NotNull(p.SetMethod));
        Assert.DoesNotContain("Resolve", typeof(CommittedComplexity).GetMethods().Select(m => m.Name));
    }

    // ── The sandbox failing is not the learner being wrong ───────────────────

    /// A measurement PRAXIS could not take is not a prediction the learner got wrong.
    [Fact]
    public async Task When_the_sandbox_cannot_run_nothing_is_resolved()
    {
        var resolver = new ComplexityResolver(new BrokenSandbox());

        var committed = ComplexityResolver.Commit("O(n)", 0.9).Value;

        var resolution = await resolver.ResolveAsync(
            new ComplexityQuestion("def work(n):\n    pass"), committed);

        Assert.False(resolution.IsSuccess);
    }

    private sealed class BrokenSandbox : ISandbox
    {
        public Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
            => Task.FromResult(Result<ExecutionResult>.Invalid("Docker is down."));
    }
}

/// **Complexity measured for real** — the learner's prediction against how the code actually scales.
[Collection("sandbox")]
public sealed class ComplexityResolverLiveTests
{
    private static ComplexityResolver Resolver()
        => new(new DockerSandbox(NullLogger<DockerSandbox>.Instance));

    private static async Task<PredictionResolution> Resolve(string code, string prediction)
    {
        var committed = ComplexityResolver.Commit(prediction, 0.8).Value;

        var result = await Resolver().ResolveAsync(new ComplexityQuestion(code), committed);

        Assert.True(result.IsSuccess, result.Message);

        return result.Value;
    }

    private const string BubbleSort =
        """
        def work(n):
            a = list(range(n, 0, -1))
            for i in range(len(a)):
                for j in range(len(a) - 1):
                    if a[j] > a[j + 1]:
                        a[j], a[j + 1] = a[j + 1], a[j]
        """;

    private const string LinearScan =
        """
        def work(n):
            total = 0
            for i in range(n):
                total += i
        """;

    /// **A nested-loop sort scales as `O(n²)`, and reality confirms it.**
    [RequiresDocker]
    public async Task A_bubble_sort_resolves_to_quadratic()
    {
        var resolution = await Resolve(BubbleSort, "O(n^2)");

        Assert.True(resolution.Correct);
        Assert.Equal("O(n^2)", resolution.GroundTruth);
    }

    /// **Predicting linear for a quadratic algorithm is caught by running it** — the learner did not
    /// see the inner loop's cost.
    [RequiresDocker]
    public async Task Predicting_linear_for_a_quadratic_algorithm_is_wrong()
    {
        var resolution = await Resolve(BubbleSort, "O(n)");

        Assert.False(resolution.Correct);
        Assert.Equal("O(n^2)", resolution.GroundTruth);

        // The Brier score reflects a confident miss.
        Assert.True(resolution.BrierScore > 0.6);
    }

    /// A single loop is `O(n)`, and a learner who says so is right.
    [RequiresDocker]
    public async Task A_single_loop_resolves_to_linear()
    {
        var resolution = await Resolve(LinearScan, "O(n)");

        Assert.True(resolution.Correct);
        Assert.Equal("O(n)", resolution.GroundTruth);
    }

    /// **Naive recursive Fibonacci is exponential, and PRAXIS says so** — without claiming to know
    /// the exact base, only that it is worse than any polynomial.
    [RequiresDocker]
    public async Task Naive_recursive_fibonacci_resolves_to_exponential()
    {
        const string fib =
            """
            def work(n):
                def fib(m):
                    if m < 2:
                        return m
                    return fib(m - 1) + fib(m - 2)
                return fib(n)
            """;

        var resolution = await Resolve(fib, "exponential");

        Assert.True(resolution.Correct);
        Assert.Contains("2^n", resolution.GroundTruth, StringComparison.Ordinal);
    }
}
