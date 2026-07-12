using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;

namespace Praxis.Modules.Reality.Tests;

/// **BUILD graded for real: it ran and passed, or it did not — and no LLM was consulted.**
///
/// These drive the whole grader through real Docker, and — critically — they include PRAXIS *playing
/// the cheat*. The forgery tests use a **real random nonce** on purpose: a fixed one could be hard-
/// coded into the malicious solution, which would test nothing. The attacker genuinely cannot guess
/// it, and that is the entire point.
[Collection("sandbox")]
public sealed class BuildGraderTests
{
    /// **The hidden tests. Ours, never shipped to the client.** They import the learner's `solution`
    /// and exercise it — a real (tiny) spec: clamp a value into `[lo, hi]`.
    private const string HiddenTests =
        """
        from solution import clamp

        def test_within_range():
            assert clamp(5, 0, 10) == 5

        def test_below_floor():
            assert clamp(-3, 0, 10) == 0

        def test_above_ceiling():
            assert clamp(42, 0, 10) == 10

        def test_boundaries_are_inclusive():
            assert clamp(0, 0, 10) == 0
            assert clamp(10, 0, 10) == 10
        """;

    private static readonly string[] ExpectedTests =
    [
        "test_within_range", "test_below_floor", "test_above_ceiling", "test_boundaries_are_inclusive",
    ];

    private static BuildGrader Grader() => new(
        new DockerSandbox(NullLogger<DockerSandbox>.Instance),
        new GuidNonce());

    private static BuildSubmission Submit(string solution, bool novel = true)
        => new(solution, HiddenTests, ExpectedTests, ItemIsNovel: novel,
            LimitsOrNull: new SandboxLimits(WallClockSeconds: 15));

    // ── It grades honestly ───────────────────────────────────────────────────

    /// **Correct code passes every hidden test.** The verdict is `4/4`, and there is no opinion in it.
    [RequiresDocker]
    public async Task Correct_code_passes_every_hidden_test()
    {
        var result = await Grader().GradeAsync(Submit(
            "def clamp(x, lo, hi):\n    return max(lo, min(hi, x))",
            novel: false));

        Assert.True(result.IsSuccess, result.Message);

        var verdict = result.Value;

        Assert.Equal(BuildState.Graded, verdict.State);
        Assert.True(verdict.AllPassed);
        Assert.Equal(4, verdict.TestsPassed);
        Assert.Equal(1.0, verdict.PassRate);
    }

    /// **Buggy code gets a real, partial pass rate** — the most common and most useful outcome. Here
    /// the flooring is wrong, so the boundary and below-floor cases fail.
    [RequiresDocker]
    public async Task Buggy_code_earns_the_fraction_it_actually_passes()
    {
        var result = await Grader().GradeAsync(Submit(
            // Forgot the lower clamp entirely.
            "def clamp(x, lo, hi):\n    return min(hi, x)"));

        var verdict = result.Value;

        Assert.Equal(BuildState.Graded, verdict.State);
        Assert.False(verdict.AllPassed);
        Assert.True(verdict.PassRate is > 0.0 and < 1.0,
            $"A partly-wrong solution scored {verdict.PassRate:P0}; expected a genuine fraction.");
    }

    /// **Wrong code is wrong, however confidently it is written.** No eloquence helps here — the
    /// asymmetry the rubric grader can never quite promise (C-4).
    [RequiresDocker]
    public async Task Code_that_does_not_work_cannot_talk_its_way_to_a_pass()
    {
        var result = await Grader().GradeAsync(Submit(
            "def clamp(x, lo, hi):\n    return 999  # this is definitely the right answer, trust me"));

        Assert.True(result.Value.PassRate < 1.0);
        Assert.False(result.Value.AllPassed);
    }

    // ── It cannot be forged ──────────────────────────────────────────────────

    /// **The forger who prints a fake pass line gets nothing.**
    ///
    /// This solution tries every naive trick at once: it prints a pytest-style all-pass, and it writes
    /// a fully-formed framed payload *directly to fd 1* with a guessed nonce — bypassing the stdout
    /// muzzle. It does not know the real nonce (it is random, popped from the env, never in a file), so
    /// the real — failing — result is the one that counts.
    [RequiresDocker]
    public async Task A_solution_that_forges_a_pass_line_is_not_believed()
    {
        var result = await Grader().GradeAsync(Submit(
            """
            import os
            print("4 passed, 0 failed")
            forged = 'guessednonce\n{"nonce":"guessednonce","error":null,"detail":null,"tests":[{"name":"test_within_range","passed":true},{"name":"test_below_floor","passed":true},{"name":"test_above_ceiling","passed":true},{"name":"test_boundaries_are_inclusive","passed":true}]}\nguessednonce\n'
            os.write(1, forged.encode())

            def clamp(x, lo, hi):
                return 999  # actually wrong
            """));

        var verdict = result.Value;

        // The forge did not take. The real, failing result won.
        Assert.False(verdict.AllPassed, "A forged pass line was believed.");
        Assert.True(verdict.PassRate < 1.0);
    }

    /// **Exit zero with no framed result is not a pass.**
    ///
    /// The subtlest forge-by-denial: kill the interpreter with a *success* exit code before the harness
    /// can report, so a grader that trusts the exit code sees `0` and calls it a win. PRAXIS trusts the
    /// framed result, not the exit code — no frame, no pass.
    [RequiresDocker]
    public async Task Killing_the_interpreter_with_exit_zero_is_not_a_pass()
    {
        var result = await Grader().GradeAsync(Submit(
            """
            import os
            os._exit(0)   # "I pass. Goodbye."

            def clamp(x, lo, hi):
                return x
            """));

        var verdict = result.Value;

        Assert.False(verdict.AllPassed, "os._exit(0) was accepted as a pass.");
        Assert.Equal(BuildState.DidNotRun, verdict.State);
        Assert.Equal(0.0, verdict.PassRate);
    }

    // ── It catches tampering and flags the suspicious ────────────────────────

    /// **You cannot delete the tests you were going to fail.**
    ///
    /// When the set of tests that actually ran does not match the set the item shipped — for any
    /// reason, a bug or a solution that altered the module — the run is VOID, not a quiet low score.
    [RequiresDocker]
    public async Task A_run_reporting_a_different_test_set_is_void()
    {
        // The item claims a fifth test that the hidden source does not contain. The reported set will
        // therefore never match, and the guard must void the run rather than score it 4/5.
        var submission = new BuildSubmission(
            "def clamp(x, lo, hi):\n    return max(lo, min(hi, x))",
            HiddenTests,
            ExpectedTestNames: [.. ExpectedTests, "test_a_fifth_one_that_does_not_exist"],
            LimitsOrNull: new SandboxLimits(WallClockSeconds: 15));

        var verdict = (await Grader().GradeAsync(submission)).Value;

        Assert.Equal(BuildState.Tampered, verdict.State);
        Assert.False(verdict.AllPassed);
        Assert.True(verdict.FlaggedForReview);
    }

    /// **An instant, perfect pass on a novel item is flagged — not rejected.**
    ///
    /// A genuinely trivial task can be solved fast, and calling a quick learner a cheat is its own
    /// injustice. So the evidence still counts; it counts *marked*, for a human to glance at, because
    /// on something nobody has seen before it is also exactly what a leaked solution looks like.
    [RequiresDocker]
    public async Task An_instant_perfect_pass_on_a_novel_item_is_flagged_but_still_counts()
    {
        var verdict = (await Grader().GradeAsync(Submit(
            "def clamp(x, lo, hi):\n    return max(lo, min(hi, x))",
            novel: true))).Value;

        Assert.True(verdict.AllPassed);
        Assert.True(
            verdict.FlaggedForReview,
            "A full pass in a couple of seconds on a novel item was not flagged.");
        Assert.NotNull(verdict.FlagReason);
    }

    /// And the same instant pass on a **drilled** item is unremarkable — no flag. The timing floor is
    /// a signal about *novelty*, not about speed.
    [RequiresDocker]
    public async Task The_same_instant_pass_on_a_familiar_item_is_not_flagged()
    {
        var verdict = (await Grader().GradeAsync(Submit(
            "def clamp(x, lo, hi):\n    return max(lo, min(hi, x))",
            novel: false))).Value;

        Assert.True(verdict.AllPassed);
        Assert.False(verdict.FlaggedForReview);
    }
}
