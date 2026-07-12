using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;

namespace Praxis.Modules.Reality.Tests;

/// **Reality grades the exam** — resolved by actually running the code.
///
/// The truth is computed here, at resolution time, and stored nowhere before it. These tests prove
/// that a correct prediction is confirmed and a wrong one is caught, by the program itself.
[Collection("sandbox")]
public sealed class BehaviourResolverTests
{
    private static BehaviourResolver Resolver()
        => new(new DockerSandbox(NullLogger<DockerSandbox>.Instance));

    private static CommittedPrediction Commit(PredictionKind kind, string answer, double confidence = 0.8)
        => BehaviourPrediction.Commit(new PredictionQuestion(kind, ""), answer, confidence).Value with
        {
            // The question's code is irrelevant to the commit; the test supplies it at resolve time.
            Kind = kind,
        };

    // ── Output ───────────────────────────────────────────────────────────────

    /// **They read the program right, and reality agrees.**
    [RequiresDocker]
    public async Task A_correct_output_prediction_is_confirmed_by_running_the_code()
    {
        var question = new PredictionQuestion(
            PredictionKind.Output,
            "for i in range(3):\n    print(i * i)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Output, "0\n1\n4"));

        Assert.True(resolution.IsSuccess, resolution.Message);
        Assert.True(resolution.Value.Correct);
        Assert.Equal("0\n1\n4", resolution.Value.GroundTruth);
    }

    /// **They misread it, and the program says so.** A subtle off-by-one — they predicted `1 2 3`
    /// where the code prints `0 1 2`.
    [RequiresDocker]
    public async Task A_wrong_output_prediction_is_caught()
    {
        var question = new PredictionQuestion(
            PredictionKind.Output,
            "for i in range(3):\n    print(i)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Output, "1\n2\n3", confidence: 0.95));

        Assert.True(resolution.Value.Correct is false);
        Assert.Equal("0\n1\n2", resolution.Value.GroundTruth);

        // Sure and wrong — the most useful outcome, and a Brier score to match.
        Assert.True(resolution.Value.SureAndWrong);
        Assert.Equal(0.9025, resolution.Value.BrierScore, tolerance: 1e-9);
    }

    /// **A trailing newline is not a wrong answer.** Predicting `42` when the code prints `42\n` is
    /// understanding the program, not a mistake — so whitespace at the end is normalised away.
    [RequiresDocker]
    public async Task Trailing_whitespace_does_not_make_a_right_answer_wrong()
    {
        var question = new PredictionQuestion(PredictionKind.Output, "print(42)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Output, "42"));

        Assert.True(resolution.Value.Correct);
    }

    // ── Raises ───────────────────────────────────────────────────────────────

    /// **They predicted the crash, and named it right.**
    [RequiresDocker]
    public async Task A_correct_exception_prediction_names_the_right_type()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "print(1 / 0)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Raises, "raises ZeroDivisionError"));

        Assert.True(resolution.Value.Correct);
        Assert.Contains("ZeroDivisionError", resolution.Value.GroundTruth, StringComparison.Ordinal);
    }

    /// **Predicting *some* error but the wrong one is not understanding why it breaks.** They said
    /// `ValueError`; it raises `ZeroDivisionError`.
    [RequiresDocker]
    public async Task Predicting_the_wrong_exception_type_is_wrong()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "print(1 / 0)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Raises, "raises ValueError"));

        Assert.False(resolution.Value.Correct);
    }

    /// **They said it would blow up, and it ran clean.**
    [RequiresDocker]
    public async Task Predicting_a_crash_that_does_not_happen_is_wrong()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "print('all fine')");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Raises, "raises RuntimeError"));

        Assert.False(resolution.Value.Correct);
        Assert.Equal("clean", resolution.Value.GroundTruth);
    }

    /// **They said it was clean, and it was.**
    [RequiresDocker]
    public async Task Predicting_clean_on_code_that_runs_clean_is_right()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "x = sum(range(10))\nprint(x)");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Raises, "clean"));

        Assert.True(resolution.Value.Correct);
    }

    /// **`raises` with no named type is satisfied by any crash** — a learner who knew it would break
    /// but was not sure of the exact exception still saw the important thing.
    [RequiresDocker]
    public async Task Predicting_a_crash_without_naming_the_type_is_satisfied_by_any_crash()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "[][1]");

        var resolution = await Resolver().ResolveAsync(
            question,
            Commit(PredictionKind.Raises, "raises"));

        Assert.True(resolution.Value.Correct);
        Assert.Contains("IndexError", resolution.Value.GroundTruth, StringComparison.Ordinal);
    }
}
