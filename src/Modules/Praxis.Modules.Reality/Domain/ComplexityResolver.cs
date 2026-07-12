using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **Predicting how code *scales* — and reality measuring it.**
///
/// The learner is shown a program that defines `work(n)` and asked its time complexity. They commit a
/// class and a confidence (the same commit-before-resolve discipline as M9.3), and then PRAXIS runs
/// the code at growing input sizes, counts the operations at each, and fits a growth curve
/// (`ComplexityFit`).
///
/// ## What is measured, and the honest constraint on it
///
/// The count is **Python-level line executions**, taken deterministically with `settrace` — so the
/// same code and the same size produce the same number every time, on every machine, which is what
/// lets a complexity verdict be a *test* rather than a flaky timing race. But it means the resolver
/// sees only work done in Python: a program that hides its cost inside a C built-in (`sorted`, a set
/// membership test) will look cheaper than it is. **An item that does that is not a valid complexity
/// item** — the whole point of teaching complexity is code whose cost is visible in its loops, and
/// that is the code these items must use. The constraint is documented here rather than worked
/// around, because the alternative (timing) trades a real, nameable limitation for a flaky one.
public sealed class ComplexityResolver
{
    private readonly ISandbox _sandbox;

    public ComplexityResolver(ISandbox sandbox) => _sandbox = sandbox;

    public const string CodeFile = "program.py";
    public const string RunnerFile = "__praxis_complexity.py";

    public async Task<Result<PredictionResolution>> ResolveAsync(
        ComplexityQuestion question,
        CommittedComplexity commitment,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(commitment);

        var job = new SandboxJob(
            Files: new Dictionary<string, string>
            {
                [CodeFile] = question.Code,
                [RunnerFile] = ComplexityHarness.Runner(),
            },
            Command: ["python", RunnerFile],

            // Complexity items run the code many times at growing sizes, so they get a longer wall
            // than a single execution. The operation cap in the harness is the real bound; this is
            // just so a genuinely slow-but-bounded run is not cut off mid-measurement.
            Limits: new SandboxLimits(WallClockSeconds: 30))
        {
            Image = question.Image,
        };

        var run = await _sandbox.RunAsync(job, ct);

        if (!run.IsSuccess)
        {
            // **The sandbox failed, not the learner.** A measurement PRAXIS could not take is not a
            // prediction the learner got wrong — and a wrong resolution here poisons their calibration
            // record, which is the one thing this Loop exists to keep honest.
            return run.CarryFailure<PredictionResolution>();
        }

        var samples = ComplexityHarness.Parse(run.Value.Stdout);

        if (samples is null)
        {
            // The harness did not report a measurement — the code had no `work(n)`, crashed on
            // import, or was killed. Nothing was measured, so nothing is resolved against the learner.
            return Result<PredictionResolution>.Invalid(
                "PRAXIS could not measure how this code scales — it did not run cleanly enough to "
                + "count. Nothing has been recorded against your prediction.");
        }

        var found = ComplexityFit.Classify(samples);

        if (found.Class == ComplexityClasses.Indeterminate)
        {
            // **PRAXIS declines to grade what it could not measure well.** Naming a class from data
            // that fits none would be the diagnostic committing the overconfidence it grades learners
            // for — so the prediction is neither confirmed nor denied.
            return Result<PredictionResolution>.Invalid(
                "The measurements did not fall cleanly onto any growth curve, so PRAXIS will not "
                + "score this one either way. That is our limitation, not your mistake.");
        }

        var correct = found.Class == commitment.Predicted;

        return Result<PredictionResolution>.Success(new PredictionResolution(
            correct,
            GroundTruth: found.Class.Label,
            Predicted: commitment.Predicted.Label,
            Confidence: commitment.Confidence));
    }

    /// **Commit.** Parse the learner's answer into a class, capture their confidence, freeze it.
    public static Result<CommittedComplexity> Commit(string answer, double confidence)
    {
        if (confidence is < 0.0 or > 1.0)
        {
            return Result<CommittedComplexity>.Invalid(
                "Confidence must be between 0 and 1, and it is not optional — a prediction PRAXIS "
                + "cannot score against how sure you were teaches you nothing about your calibration.");
        }

        var predicted = ComplexityAnswers.Parse(answer);

        if (predicted is null)
        {
            return Result<CommittedComplexity>.Invalid(
                "PRAXIS did not recognise that as a complexity class. Say something like O(n), "
                + "O(n log n), O(n^2), or 'exponential'.");
        }

        return Result<CommittedComplexity>.Success(new CommittedComplexity(predicted, confidence));
    }
}

/// The program the learner is shown. It defines `work(n)`; there is no field for its complexity,
/// because that is the thing being predicted and the thing PRAXIS will not compute until they commit.
public sealed record ComplexityQuestion(string Code)
{
    public string Image { get; init; } = "python:3.13-alpine";
}

/// **The commit — frozen, and scoreable.** Immutable, like `CommittedPrediction`: no way to edit it
/// into agreeing with the measurement after the fact.
public sealed record CommittedComplexity(ComplexityClass Predicted, double Confidence);

/// Lenient parsing of the many ways a learner writes a complexity class.
public static class ComplexityAnswers
{
    public static ComplexityClass? Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var a = answer.Trim().ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal)
            .Replace("θ", "o", StringComparison.Ordinal)   // Θ(n) and O(n) mean the same here
            .Replace("theta", "o", StringComparison.Ordinal)
            .Replace("big-o", "o", StringComparison.Ordinal);

        // Exponential / factorial first — "worse than polynomial".
        if (a.Contains("2^n", StringComparison.Ordinal) || a.Contains("2**n", StringComparison.Ordinal)
            || a.Contains("exponential", StringComparison.Ordinal) || a.Contains("factorial", StringComparison.Ordinal)
            || a.Contains("n!", StringComparison.Ordinal))
        {
            return ComplexityClasses.Exponential;
        }

        // n log n before n and before log n — it contains both substrings.
        if (a.Contains("nlogn", StringComparison.Ordinal) || a.Contains("nlog(n)", StringComparison.Ordinal)
            || a.Contains("linearithmic", StringComparison.Ordinal))
        {
            return ComplexityClasses.Linearithmic;
        }

        if (a.Contains("n^3", StringComparison.Ordinal) || a.Contains("n3", StringComparison.Ordinal)
            || a.Contains("cubic", StringComparison.Ordinal))
        {
            return ComplexityClasses.Cubic;
        }

        if (a.Contains("n^2", StringComparison.Ordinal) || a.Contains("n2", StringComparison.Ordinal)
            || a.Contains("quadratic", StringComparison.Ordinal))
        {
            return ComplexityClasses.Quadratic;
        }

        if (a.Contains("logn", StringComparison.Ordinal) || a.Contains("log(n)", StringComparison.Ordinal)
            || a.Contains("logarithmic", StringComparison.Ordinal))
        {
            return ComplexityClasses.Logarithmic;
        }

        if (a.Contains("linear", StringComparison.Ordinal)
            || a.Contains("o(n)", StringComparison.Ordinal) || a == "n")
        {
            return ComplexityClasses.Linear;
        }

        if (a.Contains("o(1)", StringComparison.Ordinal) || a.Contains("constant", StringComparison.Ordinal))
        {
            return ComplexityClasses.Constant;
        }

        return null;
    }
}
