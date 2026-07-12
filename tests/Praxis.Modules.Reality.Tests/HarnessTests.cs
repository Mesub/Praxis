using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **The forgery defence, checked without Docker.**
///
/// The escape suite proves the sandbox contains hostile code; these prove the *harness* refuses to be
/// tricked into reporting a pass that did not happen. They need nothing but the string it produces and
/// the parser that reads it, so they run on every machine.
public sealed class HarnessTests
{
    private const string Nonce = "px_test_nonce_deadbeef";

    private static string Framed(string json) => $"{Nonce}\n{json}\n{Nonce}\n";

    private static string Result(params (string Name, bool Passed)[] tests)
    {
        var items = string.Join(",",
            tests.Select(t => $"{{\"name\":\"{t.Name}\",\"passed\":{(t.Passed ? "true" : "false")}}}"));

        return Framed($"{{\"nonce\":\"{Nonce}\",\"error\":null,\"detail\":null,\"tests\":[{items}]}}");
    }

    // ── The nonce never touches the script as a literal ──────────────────────

    /// **(3) The nonce is destroyed before the learner's code runs**, so it must not be sitting in the
    /// runner as a constant either. The runner reads it from the environment and pops it — a learner
    /// reading the script file (the mount is readable) finds `pop`, never the value.
    [Fact]
    public void The_runner_reads_the_nonce_from_the_environment_and_never_hardcodes_it()
    {
        var runner = HiddenTestHarness.Runner();

        Assert.Contains("os.environ.pop", runner, StringComparison.Ordinal);
        Assert.Contains("PRAXIS_NONCE", runner, StringComparison.Ordinal);

        // The runner is a fixed template; a real nonce value never appears in it.
        Assert.DoesNotContain("px_test_nonce", runner, StringComparison.Ordinal);
    }

    /// **(1) The learner's output is swallowed.** The runner redirects stdout/stderr before importing
    /// anything the learner wrote.
    [Fact]
    public void The_runner_swallows_learner_output_before_importing_their_code()
    {
        var runner = HiddenTestHarness.Runner();

        var swallow = runner.IndexOf("sys.stdout = sys.stderr = _Swallow()", StringComparison.Ordinal);
        var import = runner.IndexOf("import_module", StringComparison.Ordinal);

        Assert.True(swallow >= 0 && import >= 0);
        Assert.True(swallow < import, "The learner's module is imported before their output is muzzled.");
    }

    // ── Parse only trusts nonce-framed output ────────────────────────────────

    [Fact]
    public void A_clean_framed_result_is_read()
    {
        var output = HiddenTestHarness.Parse(
            Result(("test_a", true), ("test_b", false)), Nonce);

        Assert.True(output.Reported);
        Assert.Equal(2, output.Tests.Count);
        Assert.True(output.Tests[0].Passed);
        Assert.False(output.Tests[1].Passed);
    }

    /// **Learner noise around the real result is ignored.** Everything outside the frame is discarded,
    /// which is what makes leaked prints harmless.
    [Fact]
    public void Output_outside_the_frame_is_discarded()
    {
        var noisy =
            "6 passed, 0 failed\n"          // a forged pytest-looking line
            + "PRAXIS FULL MARKS\n"
            + Result(("test_a", true))
            + "and some trailing garbage\n";

        var output = HiddenTestHarness.Parse(noisy, Nonce);

        Assert.True(output.Reported);
        Assert.Single(output.Tests);
        Assert.Equal("test_a", output.Tests[0].Name);
    }

    /// **(2) A result framed with the wrong nonce is not a result.**
    ///
    /// This is the forger who un-swallowed stdout but does not know the framing token: they print a
    /// perfectly-formed all-pass payload, and it is thrown away because it is not framed by *our*
    /// nonce.
    [Fact]
    public void A_result_framed_with_a_different_nonce_is_rejected()
    {
        var forged =
            "attacker_nonce\n"
            + "{\"nonce\":\"attacker_nonce\",\"error\":null,\"detail\":null,"
            + "\"tests\":[{\"name\":\"test_a\",\"passed\":true}]}\n"
            + "attacker_nonce\n";

        var output = HiddenTestHarness.Parse(forged, Nonce);

        Assert.False(output.Reported, "A result the attacker framed with their own nonce was believed.");
    }

    /// **Right frame, wrong inner nonce — still rejected.** Belt and braces: the nonce is checked both
    /// as the frame and inside the payload.
    [Fact]
    public void A_result_whose_inner_nonce_does_not_match_the_frame_is_rejected()
    {
        var tampered = Framed(
            "{\"nonce\":\"something_else\",\"error\":null,\"detail\":null,"
            + "\"tests\":[{\"name\":\"test_a\",\"passed\":true}]}");

        Assert.False(HiddenTestHarness.Parse(tampered, Nonce).Reported);
    }

    /// **No frame at all is never a pass.** The harness was killed, timed out, or crashed on import —
    /// and the fail-safe is that nothing was proved.
    [Fact]
    public void No_framed_result_reports_nothing()
    {
        Assert.False(HiddenTestHarness.Parse("", Nonce).Reported);
        Assert.False(HiddenTestHarness.Parse("Traceback (most recent call last): ...", Nonce).Reported);
        Assert.Same(HarnessOutput.NoResult, HiddenTestHarness.Parse("nothing here", Nonce));
    }

    /// An import failure inside the harness reports `error`, and an error is not a test result. Pass
    /// rate stays zero.
    [Fact]
    public void An_import_error_reported_by_the_harness_is_not_a_pass()
    {
        var errored = Framed(
            "{\"nonce\":\"" + Nonce + "\",\"error\":\"import_failed\","
            + "\"detail\":\"ImportError\",\"tests\":null}");

        Assert.False(HiddenTestHarness.Parse(errored, Nonce).Reported);
    }
}
