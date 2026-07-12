using System.Text.RegularExpressions;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **Reality grades the exam.**
///
/// The resolver runs the code — in the sandbox, because even our own item code runs where untrusted
/// code runs (M9.1), defence in depth costs nothing — and compares what it *actually did* to what the
/// learner *committed* it would do.
///
/// Note the argument it takes: a `CommittedPrediction`, which cannot be constructed without an answer
/// and a confidence. **You cannot resolve a prediction that was never made.** That is not a
/// convention enforced by a handler somewhere upstream; it is the shape of this method. A caller
/// holding no commit has nothing to pass, and a resolution is therefore proof that a commitment came
/// first.
public sealed partial class BehaviourResolver
{
    private readonly ISandbox _sandbox;

    public BehaviourResolver(ISandbox sandbox) => _sandbox = sandbox;

    public const string CodeFile = "program.py";

    public async Task<Result<PredictionResolution>> ResolveAsync(
        PredictionQuestion question,
        CommittedPrediction commitment,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(commitment);

        if (question.Kind != commitment.Kind)
        {
            // A commitment about one kind of question cannot be resolved against another. This is a
            // programming error, not a learner outcome, so it is a bug-shaped failure.
            throw new ArgumentException(
                $"A '{commitment.Kind}' prediction cannot resolve a '{question.Kind}' question.",
                nameof(commitment));
        }

        var job = new SandboxJob(
            Files: new Dictionary<string, string> { [CodeFile] = question.Code },
            Command: ["python", CodeFile],
            Limits: SandboxLimits.Default);

        var run = await _sandbox.RunAsync(job, ct);

        if (!run.IsSuccess)
        {
            // **The sandbox failed, not the learner.** Carry it intact — a Docker outage must never
            // resolve as *"your prediction was wrong"*, because a wrong resolution here poisons the
            // learner's calibration record, which is the one thing the Reality Loop exists to keep
            // honest.
            return run.CarryFailure<PredictionResolution>();
        }

        var result = run.Value;

        var (correct, groundTruth) = commitment.Kind switch
        {
            PredictionKind.Output => ResolveOutput(result, commitment.Answer),
            PredictionKind.Raises => ResolveRaises(result, commitment.Answer),
            _ => throw new ArgumentOutOfRangeException(nameof(commitment)),
        };

        return Result<PredictionResolution>.Success(new PredictionResolution(
            correct,
            groundTruth,
            commitment.Answer,
            commitment.Confidence));
    }

    /// **What did it print?** Compared after normalising trailing whitespace, because a learner who
    /// predicted `42` and meant `42\n` understood the program perfectly, and marking them wrong for a
    /// newline would be grading typing, not comprehension.
    private static (bool Correct, string GroundTruth) ResolveOutput(
        ExecutionResult result, string predicted)
    {
        var actual = Normalise(result.Stdout);

        return (string.Equals(Normalise(predicted), actual, StringComparison.Ordinal), actual);
    }

    /// **Did it blow up, and how?** The learner said `clean`, or `raises` optionally naming the
    /// exception. We read what actually happened off the exit and the traceback.
    private static (bool Correct, string GroundTruth) ResolveRaises(
        ExecutionResult result, string predicted)
    {
        // A Python traceback ends with `ExceptionType: message`. A clean exit prints none.
        var raised = result.Outcome is ExecutionOutcome.Failed && HasTraceback(result.Stderr);

        var actualType = raised ? ExceptionType(result.Stderr) : null;

        var groundTruth = raised ? $"raises {actualType}" : "clean";

        var claim = predicted.Trim();

        if (claim.StartsWith("clean", StringComparison.OrdinalIgnoreCase))
        {
            return (!raised, groundTruth);
        }

        if (!claim.StartsWith("raises", StringComparison.OrdinalIgnoreCase))
        {
            // Neither 'clean' nor 'raises' — the learner did not answer the question that was asked.
            return (false, groundTruth);
        }

        if (!raised)
        {
            return (false, groundTruth);
        }

        // They said it raises, and it did. If they also named a type, it has to be the right one — a
        // learner who predicts *some* error but the wrong one has not understood why it breaks.
        var namedType = claim["raises".Length..].Trim();

        if (namedType.Length == 0)
        {
            return (true, groundTruth);
        }

        return (string.Equals(namedType, actualType, StringComparison.OrdinalIgnoreCase), groundTruth);
    }

    private static string Normalise(string text)
        => string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                  .Split('\n')
                                  .Select(line => line.TrimEnd()))
            .TrimEnd('\n');

    private static bool HasTraceback(string stderr)
        => stderr.Contains("Traceback (most recent call last)", StringComparison.Ordinal)
           || ExceptionLine().IsMatch(stderr);

    /// The last `ExceptionType: message` line of a traceback names the exception.
    private static string? ExceptionType(string stderr)
    {
        var matches = ExceptionLine().Matches(stderr);

        return matches.Count > 0 ? matches[^1].Groups["type"].Value : null;
    }

    [GeneratedRegex(@"^(?<type>[A-Z]\w*(?:Error|Exception|Warning|Interrupt|Exit|Iteration))",
        RegexOptions.Multiline)]
    private static partial Regex ExceptionLine();
}
