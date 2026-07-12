using System.Diagnostics;

namespace Praxis.Modules.Reality.Tests;

/// **A skip is not a pass, and this makes that impossible to forget.**
///
/// The sandbox tests need real Docker, which is not present on every machine that runs the suite. The
/// wrong way to handle that is a silent `[Fact(Skip = "...")]` that goes green — because a green
/// checkmark on the *sandbox-escape suite* specifically is the most dangerous false comfort in the
/// entire product. It says "hostile code cannot break out" when what actually happened is "nobody
/// checked".
///
/// So these tests skip **by name, with a reason that says out loud what was not verified** — and the
/// reason names the thing at stake, so a person reading the run has to make a decision rather than
/// glance past a tick.
public static class DockerAvailable
{
    public const string Reason =
        "SANDBOX NOT VERIFIED: Docker is not available on this machine, so the sandbox-escape suite "
        + "did not run. This is NOT a pass. Hostile learner code has not been shown to be contained "
        + "here — it has simply not been tested. Do not ship a release whose sandbox suite only ever "
        + "skipped.";

    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            return process.WaitForExit(15_000) && process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    public static bool Yes => Probe.Value;
}

/// A `[Fact]` that turns itself into a loud, named skip when Docker is absent — never a quiet pass.
public sealed class RequiresDockerAttribute : FactAttribute
{
    public RequiresDockerAttribute()
    {
        if (!DockerAvailable.Yes)
        {
            Skip = DockerAvailable.Reason;
        }
    }
}
