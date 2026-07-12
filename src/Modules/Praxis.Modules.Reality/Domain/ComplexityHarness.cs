using System.Text.Json;

namespace Praxis.Modules.Reality.Domain;

/// **The measuring instrument: deterministic operation counting.**
///
/// The driver imports the item's `work(n)`, and for a geometric ladder of input sizes installs a
/// `settrace` line-counter, calls `work(n)`, and records how many Python lines executed. Same code,
/// same size, **same count, every time** — which is the property that lets a complexity verdict be a
/// test rather than a timing race.
///
/// It starts small and doubles, so an exponential algorithm produces several sub-cap samples *before*
/// it explodes (starting at a large size would just cap on the first run and measure nothing). It
/// stops the moment a run crosses the operation cap — that cap is a signal, not a failure, and the
/// `Capped` sample is exactly what the exponential detector reads.
///
/// There is no nonce and no output-swallowing here, unlike the BUILD harness (M9.2): the code being
/// measured is the **item's**, authored by PRAXIS, not the learner's. The learner supplies only a
/// prediction. So there is nothing hostile to defend against — the sandbox is defence in depth, not
/// the primary guard.
public static class ComplexityHarness
{
    /// Above this many counted operations, a run is treated as explosive and cut off. High enough
    /// that any polynomial at the sizes below stays well under it; low enough that `settrace` gets
    /// through it in a second or two.
    public const int OperationCap = 3_000_000;

    /// The size ladder. Small enough at the bottom that an exponential gets a few points in; wide
    /// enough at the top that a polynomial's asymptotic shape dominates its constant overhead.
    private static readonly int[] Sizes = [4, 8, 16, 32, 64, 128, 256, 512, 1024];

    public static string Runner()
        => $$"""
            import sys, json, importlib

            OP_CAP = {{OperationCap}}
            SIZES = [{{string.Join(", ", Sizes)}}]

            class _Stop(Exception):
                pass

            try:
                program = importlib.import_module("{{ComplexityResolver.CodeFile[..^".py".Length]}}")
                work = program.work
            except Exception:
                # No work(n) to measure. Report nothing rather than a bogus zero — the resolver
                # treats an absent measurement as "not measured", never as a learner outcome.
                print(json.dumps({"samples": None}))
                sys.exit(0)

            def count(n):
                total = [0]
                def tracer(frame, event, arg):
                    if event == "line":
                        total[0] += 1
                        if total[0] > OP_CAP:
                            raise _Stop()
                    return tracer
                sys.settrace(tracer)
                try:
                    work(n)
                    capped = False
                except _Stop:
                    capped = True
                except Exception:
                    sys.settrace(None)
                    return None, False
                finally:
                    sys.settrace(None)
                return total[0], capped

            samples = []
            for n in SIZES:
                ops, capped = count(n)
                if ops is None:
                    # The code raised at this size. Stop — we cannot measure past a crash.
                    break
                samples.append({"n": n, "ops": ops, "capped": capped})
                if capped:
                    break

            print(json.dumps({"samples": samples}))
            """;

    /// Read the measured samples back. Returns `null` when the harness reported no measurement — an
    /// absent `work(n)`, a crash, or a killed interpreter — which the resolver must not treat as a
    /// learner outcome.
    public static IReadOnlyList<OperationSample>? Parse(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);

        // The measurement is the last JSON line the driver prints; anything before it is noise.
        foreach (var line in stdout.Split('\n').Reverse())
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith('{'))
            {
                continue;
            }

            HarnessReport? report;

            try
            {
                report = JsonSerializer.Deserialize<HarnessReport>(trimmed, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (report?.Samples is null)
            {
                return null;
            }

            return [.. report.Samples.Select(s => new OperationSample(s.N, s.Ops) { Capped = s.Capped })];
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record HarnessReport(IReadOnlyList<HarnessSample>? Samples);

    private sealed record HarnessSample(int N, long Ops, bool Capped);
}
