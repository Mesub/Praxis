using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **`BUILD` evidence: it either ran and passed, or it did not. No LLM decides.**
///
/// This is the one grader in PRAXIS that cannot be flattered, cannot be jailbroken, and cannot be
/// argued with — because it is not an opinion. The learner's code runs against hidden tests inside
/// the sandbox (M9.1), and the verdict is **arithmetic over exit conditions**: how many of *our*
/// tests their code satisfied. A perfectly-worded wrong answer scores zero; an ugly right one scores
/// full marks. That is exactly the asymmetry the rubric grader (M5.4) can never quite guarantee, and
/// it is why `BUILD` is one of the three evidence types that can actually confer mastery (C-4).
///
/// ## What could go wrong, and what stops it
///
/// **Forgery** — a malicious solution printing a fake pass line — is stopped by the harness, which
/// swallows the learner's output and frames the real verdict with a nonce they never see
/// (`HiddenTestHarness`).
///
/// **Tampering** — a solution that monkeypatches the test module so fewer tests run, or renames them
/// — is caught here: the grader knows the exact set of test names it shipped, and **a run that
/// reports a different set is VOID**, not a pass. You cannot delete the tests you were going to fail.
///
/// **Instant perfection** — a full pass in less time than a person could read the spec — is not
/// *rejected* (a genuinely trivial task can be solved fast, and calling a fast learner a cheat is its
/// own injustice) but **flagged for review**, because on a novel item it is the signature of a leaked
/// solution or a frame-walking exploit. The evidence still lands; it lands *marked*.
///
/// **The sandbox itself failing** is not the learner's problem and never becomes a verdict — it
/// travels back as a `Result` failure (M9.1), so a Docker outage cannot be recorded as "your code
/// failed".
public sealed class BuildGrader
{
    private readonly ISandbox _sandbox;
    private readonly INonceSource _nonce;

    public BuildGrader(ISandbox sandbox, INonceSource nonce) => (_sandbox, _nonce) = (sandbox, nonce);

    /// A solution below this, submitted in under `TimingFloorSeconds`, on a novel item, is flagged —
    /// **not for being wrong, but for being suspiciously right suspiciously fast.**
    public const double SuspiciousPassRate = 1.0;

    /// Faster than a person can plausibly read the spec, think, and type a correct answer.
    public const int TimingFloorSeconds = 20;

    public async Task<Result<BuildVerdict>> GradeAsync(BuildSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (submission.ExpectedTestNames.Count == 0)
        {
            // An item with no hidden tests cannot produce BUILD evidence. Refusing here is better than
            // grading against nothing and calling the empty pass rate a mastery signal.
            throw new ArgumentException(
                "A BUILD item with no hidden tests grades nothing.", nameof(submission));
        }

        var nonce = _nonce.Next();

        var job = new SandboxJob(
            Files: new Dictionary<string, string>
            {
                [HiddenTestHarness.SolutionFile] = submission.SolutionSource,
                [HiddenTestHarness.HiddenTestsFile] = submission.HiddenTestSource,
                [HiddenTestHarness.RunnerFile] = HiddenTestHarness.Runner(),
            },
            Command: ["python", HiddenTestHarness.RunnerFile],
            Limits: submission.Limits)
        {
            Image = submission.Image,
        };

        // The nonce is handed to the container in the environment, where the harness pops it before a
        // line of learner code runs. It is prefixed onto the command via the sandbox's env plumbing —
        // see `WithNonce`.
        var result = await _sandbox.RunAsync(job.WithNonce(nonce), ct);

        if (!result.IsSuccess)
        {
            // **The sandbox failed, not the learner.** Carry it intact — it must not become a verdict.
            return result.CarryFailure<BuildVerdict>();
        }

        var run = result.Value;

        var parsed = HiddenTestHarness.Parse(run.Stdout, nonce);

        if (!parsed.Reported)
        {
            // No framed result. The harness never got to speak — killed, crashed on import, timed out,
            // or a forge-by-denial. **The fail-safe is a clean, honest failure**, with the pass rate
            // at zero and nothing crossing the Evidence Ceiling. It is never a pass.
            return Result<BuildVerdict>.Success(
                BuildVerdict.DidNotReport(run.Outcome, run.DurationMs, run.Stderr));
        }

        // ── Tamper check: exactly the tests we shipped, no more, no fewer ────
        var reported = parsed.Tests.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var expected = submission.ExpectedTestNames.ToHashSet(StringComparer.Ordinal);

        if (!reported.SetEquals(expected))
        {
            // The solution changed which tests exist. **You cannot delete the tests you were going to
            // fail.** VOID — a real, named outcome, and not a low pass rate that would quietly count.
            return Result<BuildVerdict>.Success(
                BuildVerdict.Tampered(run.DurationMs, expected.Count, reported.Count));
        }

        var passed = parsed.Tests.Count(t => t.Passed);
        var total = parsed.Tests.Count;

        var suspicious = submission.ItemIsNovel
            && passed == total
            && (run.DurationMs / 1000.0) < TimingFloorSeconds;

        return Result<BuildVerdict>.Success(new BuildVerdict(
            State: BuildState.Graded,
            TestsPassed: passed,
            TestsTotal: total,
            PassRate: (double)passed / total,
            DurationMs: run.DurationMs,
            FlaggedForReview: suspicious,
            FlagReason: suspicious
                ? "A full pass in under twenty seconds on an item nobody has seen before. That may "
                  + "just be a task you found easy — but it is also what a leaked solution looks like, "
                  + "so a human will glance at it. Your evidence still counts."
                : null,
            Tests: parsed.Tests));
    }
}

/// A learner's submission, and the hidden tests it will be run against.
public sealed record BuildSubmission(
    /// What the learner wrote. Runs as `solution.py`, imported by the hidden tests.
    string SolutionSource,

    /// **Ours, and never shipped to the client.** The learner has seen the public tests; these are
    /// the ones that decide the verdict, and they exist only on the server and inside the sandbox.
    string HiddenTestSource,

    /// The exact set of `test_*` names in `HiddenTestSource`. The grader compares the reported set
    /// against this to catch a solution that deletes or renames tests.
    IReadOnlyList<string> ExpectedTestNames,

    /// A never-before-seen item. **Only novel items get the timing floor** — an instant pass on a
    /// concept the learner has drilled fifty times is unremarkable; on a novel one it is a signal.
    bool ItemIsNovel = true,

    SandboxLimits? LimitsOrNull = null)
{
    public string Image { get; init; } = "python:3.13-alpine";

    public SandboxLimits Limits => LimitsOrNull ?? SandboxLimits.Default;
}

/// **The verdict, and there is nothing here an LLM produced.**
public sealed record BuildVerdict(
    BuildState State,
    int TestsPassed,
    int TestsTotal,
    double PassRate,
    int DurationMs,
    bool FlaggedForReview = false,
    string? FlagReason = null,
    IReadOnlyList<TestOutcome>? Tests = null)
{
    /// **Every hidden test satisfied.** The only thing that counts as a complete BUILD.
    public bool AllPassed => State is BuildState.Graded && TestsPassed == TestsTotal && TestsTotal > 0;

    /// The harness never reported — killed, timed out, crashed on import, or a forge-by-denial. Pass
    /// rate zero, and it never reads as a pass.
    public static BuildVerdict DidNotReport(ExecutionOutcome outcome, int durationMs, string stderr)
        => new(
            State: outcome is ExecutionOutcome.TimedOut ? BuildState.TimedOut : BuildState.DidNotRun,
            TestsPassed: 0,
            TestsTotal: 0,
            PassRate: 0.0,
            DurationMs: durationMs,
            Tests: []);

    /// The reported test set did not match what we shipped. **VOID**, and no evidence is produced.
    public static BuildVerdict Tampered(int durationMs, int expected, int reported)
        => new(
            State: BuildState.Tampered,
            TestsPassed: 0,
            TestsTotal: expected,
            PassRate: 0.0,
            DurationMs: durationMs,
            FlaggedForReview: true,
            FlagReason: $"The run reported {reported} tests; this item ships {expected}. The solution "
                        + "changed the test set, and the submission is void. You cannot delete the "
                        + "tests you were going to fail.");
}

public enum BuildState
{
    /// It ran against the real, unaltered hidden suite, and the pass rate is a fact.
    Graded = 1,

    /// The harness never reported. Not the learner's mastery, and not a pass.
    DidNotRun = 2,

    /// It did not finish inside the wall clock.
    TimedOut = 3,

    /// The test set was altered. Void, and flagged.
    Tampered = 4,
}

/// A per-run nonce, generated on the host. Behind an interface so grading stays deterministic under
/// test — the whole forgery defence rests on this being unguessable in production and *fixed* in a
/// test, and a hard-coded `Guid.NewGuid()` would be neither auditable nor testable.
public interface INonceSource
{
    string Next();
}
