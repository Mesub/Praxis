using Praxis.Modules.Reality.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Tests;

/// **The commit, and why its shape is the guarantee** — checked without Docker.
///
/// The whole integrity of `PREDICT` evidence is *when* things happen: the learner commits an answer
/// and a confidence, and only then is the truth computed. These tests hold that discipline in place at
/// the type level, where a handler cannot quietly route around it.
public sealed class BehaviourPredictionTests
{
    private static PredictionQuestion Output(string code = "print('x')")
        => new(PredictionKind.Output, code);

    // ── Confidence is mandatory ──────────────────────────────────────────────

    /// **A forecast without a probability is unscoreable**, and the commit refuses to produce one.
    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public void A_prediction_without_a_valid_confidence_is_not_a_prediction(double confidence)
    {
        var committed = BehaviourPrediction.Commit(Output(), "x", confidence);

        Assert.False(committed.IsSuccess);
        Assert.Contains("not optional", committed.Message!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void A_confidence_on_the_scale_is_accepted(double confidence)
    {
        Assert.True(BehaviourPrediction.Commit(Output(), "x", confidence).IsSuccess);
    }

    [Fact]
    public void A_prediction_with_no_claim_is_refused()
    {
        var committed = BehaviourPrediction.Commit(Output(), "   ", 0.8);

        Assert.False(committed.IsSuccess);
    }

    // ── The commit is frozen ─────────────────────────────────────────────────

    /// **There is no way to edit a committed prediction into agreeing with the outcome.**
    ///
    /// No public setter, no `Resolve` on the commit itself, no mutable `Correct`. If any of these
    /// existed, the commit-before-resolve guarantee would be a suggestion — so their *absence* is
    /// asserted, the same way the item bank asserts the absence of an answer column (M7.7a).
    [Fact]
    public void A_committed_prediction_cannot_be_mutated_after_the_fact()
    {
        var writable = typeof(CommittedPrediction)
            .GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true && p.SetMethod?.IsInitOnly() != true)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(writable);

        foreach (var escape in new[] { "Resolve", "SetOutcome", "MarkCorrect", "Grade" })
        {
            Assert.DoesNotContain(escape, typeof(CommittedPrediction).GetMethods().Select(m => m.Name));
        }
    }

    /// **You cannot resolve a prediction that was never made.** The resolver's signature *takes* a
    /// `CommittedPrediction` — a caller holding no commit has nothing to pass — so a resolution is
    /// proof a commitment came first, enforced by the compiler rather than a code review.
    [Fact]
    public void The_resolver_cannot_be_called_without_a_commitment()
    {
        var resolve = typeof(BehaviourResolver).GetMethod(nameof(BehaviourResolver.ResolveAsync))!;

        Assert.Contains(
            resolve.GetParameters(),
            p => p.ParameterType == typeof(CommittedPrediction));
    }

    // ── Brier is a pure function of the resolution ───────────────────────────

    /// **Sure and right is nearly free; sure and wrong is the expensive one.** That asymmetry is the
    /// whole lesson of calibration, and it is arithmetic, not opinion.
    [Theory]
    [InlineData(true, 1.0, 0.0)]     // certain and right — perfect
    [InlineData(false, 0.0, 0.0)]    // certain it was wrong, and it was — also perfect
    [InlineData(false, 1.0, 1.0)]    // certain and wrong — the worst score there is
    [InlineData(true, 0.5, 0.25)]    // a coin-flip, right
    [InlineData(false, 0.9, 0.81)]   // 90% sure, and wrong
    public void The_Brier_score_rewards_calibration_not_correctness(
        bool correct, double confidence, double expected)
    {
        var resolution = new PredictionResolution(correct, "truth", "claim", confidence);

        Assert.Equal(expected, resolution.BrierScore, tolerance: 1e-9);
    }

    /// **"You were sure, and you were wrong."** The sentence the learner most needs, sayable only
    /// because the confidence came first (C-6).
    [Fact]
    public void Sure_and_wrong_is_flagged_and_unsure_and_wrong_is_not()
    {
        Assert.True(new PredictionResolution(false, "t", "p", 0.9).SureAndWrong);
        Assert.False(new PredictionResolution(false, "t", "p", 0.3).SureAndWrong);

        // Being wrong when you knew you might be is a completely healthy state — not flagged.
        Assert.False(new PredictionResolution(true, "t", "p", 0.9).SureAndWrong);
    }

    // ── The sandbox failing is not the learner being wrong ───────────────────

    /// **A Docker outage must never resolve as "your prediction was wrong."**
    ///
    /// A wrong resolution here does not merely misgrade one item — it **poisons the learner's
    /// calibration record**, which is the one thing the Reality Loop exists to keep honest. So when the
    /// sandbox cannot run, the failure travels back intact; it does not become a verdict. No Docker
    /// needed to prove it — a sandbox that always fails is all it takes.
    [Fact]
    public async Task When_the_sandbox_cannot_run_nothing_is_resolved_against_the_learner()
    {
        var resolver = new BehaviourResolver(new BrokenSandbox());

        var committed = BehaviourPrediction.Commit(Output("print('x')"), "x", 0.9).Value;

        var resolution = await resolver.ResolveAsync(Output("print('x')"), committed);

        Assert.False(
            resolution.IsSuccess,
            "A sandbox outage was turned into a resolution — and a learner's calibration record was "
            + "just poisoned by our infrastructure rather than their reasoning.");
    }

    private sealed class BrokenSandbox : ISandbox
    {
        public Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
            => Task.FromResult(Result<ExecutionResult>.Invalid("Docker is down."));
    }
}

file static class PropertyInfoExtensions
{
    public static bool IsInitOnly(this System.Reflection.MethodInfo setter)
        => setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
