using System.Globalization;

namespace Praxis.Modules.Reality.Domain;

/// **The runner that stands between a learner's solution and its verdict — and the forgery it exists
/// to stop.**
///
/// A `BUILD` is graded by running the learner's code against **hidden tests** they never see. The
/// obvious implementation — run pytest, parse "5 passed" off stdout — is **forgeable in one line**,
/// because the learner's solution runs *in the same Python interpreter as the tests*. A malicious
/// `allow()` can simply `print("6 passed")` and exit, and a grader that reads stdout believes it.
///
/// So the harness treats the learner's own program output as **hostile**, and derives the verdict
/// from a channel the learner cannot write to:
///
/// ## 1. The learner's stdout is swallowed
///
/// Before a single line of learner code runs — including the top-level code that runs on *import* —
/// real stdout and stderr are redirected to `/dev/null`. Anything the solution prints, whether an
/// honest debug line or a forged `"6 passed"`, goes nowhere. Only the harness, after the tests are
/// done and stdout is restored, writes the result.
///
/// ## 2. The result is framed by a nonce the learner never sees
///
/// The verdict is printed as `<nonce>{json}<nonce>`, where the nonce is generated fresh **on the
/// host** for this one run. The C# side accepts *only* text framed by the nonce it generated. A
/// learner who somehow un-swallows stdout still cannot forge a result, because they do not know the
/// framing token.
///
/// ## 3. And the nonce is destroyed before the learner's code can read it
///
/// The nonce arrives in an environment variable, and the **first thing** the harness does — before
/// importing anything the learner wrote — is `os.environ.pop` it. It then lives only as a local in
/// the harness's own frame. It is *not* written into this script as a literal, so reading the script
/// file (the mount is readable) reveals only the word `pop`, never the value.
///
/// ## The residual, stated honestly
///
/// A determined in-process attacker can still walk the interpreter's call stack (`sys._getframe`) to
/// read the harness's locals, recover the nonce, and `os.write(1, ...)` a forged frame past the
/// stdout redirect. **That is a real hole**, and closing it fully needs per-test process isolation
/// (fork a child per test so the solution cannot reach the runner's memory at all), which is future
/// hardening. Until then it is defended in depth by the **timing floor** (`BuildGrader` flags an
/// instant-perfect submission on a novel item — a frame-walking exploit is not something a person
/// types in nine seconds) and, later, a solution-similarity check. This is written down rather than
/// papered over, because the next person to read this will assume the nonce alone is enough. It is
/// not.
public static class HiddenTestHarness
{
    /// The conventional filenames on the read-only mount. The learner's code and the hidden tests
    /// arrive the same way and are equally unwritable.
    public const string SolutionFile = "solution.py";
    public const string HiddenTestsFile = "hidden_tests.py";
    public const string RunnerFile = "__praxis_runner.py";

    public const string NonceEnvVar = "PRAXIS_NONCE";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// Build the runner script. **Pure** — no Docker, no nonce generation, no clock — so the forgery
    /// defences can be reasoned about and tested on any machine.
    ///
    /// The nonce is *not* baked in here; it is read from the environment at runtime and popped. This
    /// method only needs to know the env var's name.
    public static string Runner()
        => $$"""
            import os, sys, json, importlib, traceback

            # (3) Destroy the nonce before any learner code can run. After this line it exists only
            # as a local in this frame; it is nowhere in this file as a literal.
            _nonce = os.environ.pop({{Quote(NonceEnvVar)}}, None)

            class _Swallow:
                def write(self, *_a, **_k): return 0
                def flush(self): pass

            def _run():
                results = []

                # (1) From here until the tests are done, everything the learner's code prints goes
                # nowhere. This covers import-time top-level code, not just the test bodies.
                real_out, real_err = sys.stdout, sys.stderr
                sys.stdout = sys.stderr = _Swallow()

                try:
                    tests = importlib.import_module({{Quote(HiddenTestsModule)}})
                except Exception:
                    sys.stdout, sys.stderr = real_out, real_err
                    return None, "import_failed", traceback.format_exc(limit=3)

                names = sorted(n for n in dir(tests) if n.startswith("test_"))

                for name in names:
                    try:
                        getattr(tests, name)()
                        results.append((name, True))
                    except Exception:
                        results.append((name, False))

                sys.stdout, sys.stderr = real_out, real_err
                return results, None, None

            results, error, detail = _run()

            payload = {
                "nonce": _nonce,
                "error": error,
                "detail": detail,
                "tests": None if results is None else [
                    {"name": n, "passed": p} for (n, p) in results
                ],
            }

            # (2) The only trustworthy bytes in this whole run: framed by a token the learner never
            # saw. The C# side accepts nothing that is not between two copies of it.
            sys.stdout.write(_nonce + "\n")
            sys.stdout.write(json.dumps(payload))
            sys.stdout.write("\n" + _nonce + "\n")
            sys.stdout.flush()
            """;

    /// The module name the runner imports — the hidden-tests file without its extension.
    private static string HiddenTestsModule
        => HiddenTestsFile[..^".py".Length];

    private static string Quote(string s)
        => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// Pull the framed result back out of the container's stdout.
    ///
    /// **Everything not between the two nonce markers is discarded**, because everything not between
    /// them is either the learner's swallowed noise that leaked, or an attempt to forge one. A result
    /// that is not framed by *our* nonce is not a result.
    public static HarnessOutput Parse(string stdout, string nonce)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        var marker = nonce + "\n";

        var open = stdout.IndexOf(marker, StringComparison.Ordinal);

        if (open < 0)
        {
            // No framed result at all. **This is never a pass.** The harness did not get to report —
            // the interpreter was killed, the import blew up, or somebody tried to forge by denial —
            // and the fail-safe is that nothing was proved.
            return HarnessOutput.NoResult;
        }

        var bodyStart = open + marker.Length;

        var close = stdout.IndexOf("\n" + marker, bodyStart, StringComparison.Ordinal);

        if (close < 0)
        {
            return HarnessOutput.NoResult;
        }

        var json = stdout[bodyStart..close];

        HarnessPayload? payload;

        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<HarnessPayload>(json, JsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return HarnessOutput.NoResult;
        }

        if (payload is null)
        {
            return HarnessOutput.NoResult;
        }

        // The nonce is *inside* the payload as well as framing it. A forger who learned the framing
        // token would also have to have put the right nonce in the JSON — belt and braces on the one
        // thing that matters.
        if (!string.Equals(payload.Nonce, nonce, StringComparison.Ordinal))
        {
            return HarnessOutput.NoResult;
        }

        if (payload.Error is not null || payload.Tests is null)
        {
            return HarnessOutput.NoResult;
        }

        return new HarnessOutput(
            Reported: true,
            Tests: [.. payload.Tests.Select(t => new TestOutcome(t.Name, t.Passed))]);
    }

    private sealed record HarnessPayload(
        string? Nonce,
        string? Error,
        string? Detail,
        IReadOnlyList<HarnessTest>? Tests);

    private sealed record HarnessTest(string Name, bool Passed);
}

/// What the harness reported — or the fact that it did not.
public sealed record HarnessOutput(bool Reported, IReadOnlyList<TestOutcome> Tests)
{
    /// **The fail-safe.** No framed result: nothing was proved, and it must never read as a pass.
    public static HarnessOutput NoResult { get; } = new(false, []);
}

public sealed record TestOutcome(string Name, bool Passed);
