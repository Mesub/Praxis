using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Praxis.Modules.Reality.Domain;
using Praxis.Shared;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Praxis.Modules.Reality.Infrastructure;

/// **The walls, actually built.**
///
/// Read the argument list in `Arguments`. Every flag on it is there because of a specific thing a
/// hostile submission would otherwise do, and the comments say which. This is the one file in PRAXIS
/// where the threat model is a person rather than a mistake.
///
/// ## Why the docker CLI and not the API
///
/// A reviewer can read this and see exactly what the container gets. The Docker API client would put a
/// serialisation layer between the reader and the flags — and **every guarantee in this system needs a
/// reviewer to be able to read it and see that it holds.** A sandbox nobody can audit is a sandbox
/// nobody should trust.
public sealed partial class DockerSandbox : ISandbox
{
    private readonly ILogger<DockerSandbox> _logger;

    public DockerSandbox(ILogger<DockerSandbox> logger) => _logger = logger;

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The sandbox could not run. No evidence has been produced.")]
    private partial void LogSandboxFailed(Exception e);

    public async Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var workspace = Directory.CreateTempSubdirectory("praxis-sandbox-");

        var container = $"praxis-{Guid.CreateVersion7():N}";

        try
        {
            await StageAsync(job, workspace.FullName, ct);

            return await RunContainerAsync(job, workspace.FullName, container, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // **The sandbox failed, not the learner.** This comes back as a `Result` failure, and
            // there is no member of `ExecutionOutcome` it could be mistaken for — because a learner
            // marked down for our Docker daemon restarting would carry it in their Twin forever and
            // never find out why.
            LogSandboxFailed(e);

            return Result<ExecutionResult>.Invalid(Unavailable);
        }
        finally
        {
            await KillAsync(container);

            Cleanup(workspace);
        }
    }

    /// **What the learner's code cannot do, and the flag that stops it.**
    ///
    /// Adding to this list is easy. Removing from it requires explaining, out loud, which attack you
    /// have decided to allow.
    internal static string Arguments(SandboxJob job, string workspace, string container)
    {
        var limits = job.Limits;

        var flags = new List<string>
        {
            "run", "--rm", $"--name={container}",

            // ── There is no network. Not filtered — absent. ──────────────────
            //
            // The hidden tests are the entire basis of BUILD evidence, and a container that can reach
            // the internet can fetch the reference solution, exfiltrate the tests it was handed, or
            // phone home with somebody else's data. A network *filter* can be misconfigured. **An
            // absent interface cannot.**
            "--network=none",

            // ── No privileges, of any kind ───────────────────────────────────
            "--cap-drop=ALL",
            "--security-opt=no-new-privileges",

            // A uid that exists nowhere in our system. `nobody`, and not even by name.
            "--user=65534:65534",

            // ── Nothing is writable except one small scratch mount ───────────
            //
            // The learner's code and the hidden tests arrive on the same read-only mount, and are
            // equally unwritable. A submission that tries to rewrite the test file asserting it passed
            // finds that it cannot.
            "--read-only",
            $"--tmpfs=/tmp:rw,noexec,nosuid,size={Math.Max(16, limits.MemoryMb / 4)}m",
            $"--volume={workspace}:/work:ro",
            "--workdir=/work",

            // ── The walls ────────────────────────────────────────────────────
            $"--memory={limits.MemoryMb}m",

            // **Swap equal to memory means no swap.** Without this, a container "limited" to 256 MB
            // happily allocates gigabytes of swap and takes the host's disk with it.
            $"--memory-swap={limits.MemoryMb}m",

            $"--cpus={limits.Cpus.ToString(CultureInfo.InvariantCulture)}",

            // A fork bomb hits this number, not the host.
            $"--pids-limit={limits.MaxProcesses}",

            // ── No secrets, and no inherited environment ─────────────────────
            //
            // The container gets exactly what Python needs and nothing else. Our API keys live in the
            // host process; a container with no network could not use them anyway, and **both** are
            // true because one of them is going to be wrong one day.
            "--env=PYTHONDONTWRITEBYTECODE=1",
            "--env=PYTHONUNBUFFERED=1",
            "--env=HOME=/tmp",

            job.Image,
        };

        flags.AddRange(job.Command);

        return string.Join(' ', flags.Select(Quote));
    }

    private static async Task<Result<ExecutionResult>> RunContainerAsync(
        SandboxJob job, string workspace, string container, CancellationToken ct)
    {
        var start = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("docker", Arguments(job, workspace, container))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
        {
            return Result<ExecutionResult>.Invalid(Unavailable);
        }

        var stdout = ReadCappedAsync(process.StandardOutput, job.Limits.MaxOutputBytes, ct);
        var stderr = ReadCappedAsync(process.StandardError, job.Limits.MaxOutputBytes, ct);

        using var wall = CancellationTokenSource.CreateLinkedTokenSource(ct);

        wall.CancelAfter(TimeSpan.FromSeconds(job.Limits.WallClockSeconds));

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(wall.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // **The learner's code did not finish.** That is a fact about their code — an
            // implementation that cannot answer in ten seconds has said something true about itself,
            // and an infinite loop is a bug rather than a mystery.
            timedOut = true;

            await KillAsync(container);
        }

        var (outText, outCut) = await stdout;
        var (errText, errCut) = await stderr;

        start.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;

        // **137 is the kernel killing the container**: SIGKILL, which under a memory cgroup means the
        // OOM killer. Reported as what it is — a fact about how much the code tried to allocate — and
        // not as a mysterious failure the learner has no way to interpret.
        var outcome = timedOut
            ? ExecutionOutcome.TimedOut
            : exitCode switch
            {
                0 => ExecutionOutcome.Passed,
                137 => ExecutionOutcome.OutOfMemory,
                _ => ExecutionOutcome.Failed,
            };

        return Result<ExecutionResult>.Success(
            new ExecutionResult(outcome, exitCode, outText, errText, (int)start.ElapsedMilliseconds)
            {
                OutputTruncated = outCut || errCut,
            });
    }

    /// **A program that prints forever will exhaust the machine collecting it.**
    ///
    /// That is a denial-of-service against every other learner on the box, and it costs one integer to
    /// prevent. The truncation is *recorded*, because a cut-off stack trace that looks complete is
    /// worse than one that admits it was cut off.
    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(
        StreamReader reader, int cap, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];

        while (text.Length < cap)
        {
            var read = await reader.ReadAsync(buffer, ct);

            if (read == 0)
            {
                return (text.ToString(), false);
            }

            text.Append(buffer, 0, Math.Min(read, cap - text.Length));
        }

        // Drain the rest so the child does not block on a full pipe forever — but keep none of it.
        _ = Task.Run(async () =>
        {
            try
            {
                await reader.ReadToEndAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                // The pipe closed under us. Nothing to do, and nothing worth logging.
            }
        }, CancellationToken.None);

        return (text.ToString(), true);
    }

    private static async Task StageAsync(SandboxJob job, string workspace, CancellationToken ct)
    {
        foreach (var (path, contents) in job.Files)
        {
            // **A learner-supplied filename is a path traversal waiting to happen.** `../../etc/cron.d`
            // is a perfectly valid dictionary key.
            var full = Path.GetFullPath(Path.Combine(workspace, path));

            if (!full.StartsWith(workspace, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"The file '{path}' escapes the workspace. It has not been written.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            await File.WriteAllTextAsync(full, contents, ct);
        }
    }

    /// Belt and braces. `--rm` should have taken it, and the wall-clock path definitely needs this.
    private static async Task KillAsync(string container)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo("docker", $"kill {container}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (kill is not null)
            {
                await kill.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Docker is gone, or the container already exited. Either way there is nothing to kill.
        }
    }

    private static void Cleanup(DirectoryInfo workspace)
    {
        try
        {
            workspace.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leaked temp directory is a housekeeping problem. Throwing here would turn it into a
            // learner-facing failure, which it is not.
        }
    }

    /// Windows needs the quotes; Linux does not mind them.
    private static string Quote(string argument)
        => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;

    /// **Said out loud, and never turned into a verdict.**
    private const string Unavailable =
        "PRAXIS could not run your code, and the problem is at our end rather than yours. **Nothing "
        + "has been recorded against you** — a BUILD is evidence that your code ran, and it did not "
        + "run, so there is nothing to be evidence of. Try again in a moment.";
}
