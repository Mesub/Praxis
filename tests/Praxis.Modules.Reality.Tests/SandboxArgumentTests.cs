using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;

namespace Praxis.Modules.Reality.Tests;

/// **The walls, checked without Docker.**
///
/// The escape suite proves the sandbox *actually contains* hostile code, but it needs a Docker daemon
/// and skips loudly where there is none. These tests need nothing: they assert that the docker command
/// line PRAXIS builds carries every wall, so that **the specification of the sandbox is verified on
/// every machine, even the ones that cannot run it.**
///
/// A regression here — somebody quietly dropping `--network=none` in a refactor — fails the build
/// everywhere, not just on the CI box that happens to have Docker.
public sealed class SandboxArgumentTests
{
    private static string Args(SandboxLimits? limits = null)
        => DockerSandbox.Arguments(
            new SandboxJob(
                Files: new Dictionary<string, string> { ["x.py"] = "print(1)" },
                Command: ["python", "x.py"],
                Limits: limits ?? SandboxLimits.Default),
            workspace: "/tmp/ws",
            container: "praxis-test");

    [Fact]
    public void There_is_no_network()
    {
        Assert.Contains("--network=none", Args(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_capability_is_dropped_and_no_new_ones_can_be_gained()
    {
        var args = Args();

        Assert.Contains("--cap-drop=ALL", args, StringComparison.Ordinal);
        Assert.Contains("--security-opt=no-new-privileges", args, StringComparison.Ordinal);
    }

    [Fact]
    public void It_does_not_run_as_root()
    {
        Assert.Contains("--user=65534:65534", Args(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_filesystem_is_read_only_and_the_workspace_is_mounted_read_only()
    {
        var args = Args();

        Assert.Contains("--read-only", args, StringComparison.Ordinal);
        Assert.Contains(":/work:ro", args, StringComparison.Ordinal);
    }

    /// **Swap equal to memory means no swap.** Without it, a container "limited" to 256 MB allocates
    /// gigabytes of swap and takes the host's disk with it — the limit that looks present and does
    /// nothing.
    [Fact]
    public void Memory_is_capped_and_swap_is_disabled()
    {
        var args = Args(new SandboxLimits(MemoryMb: 200));

        Assert.Contains("--memory=200m", args, StringComparison.Ordinal);
        Assert.Contains("--memory-swap=200m", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Processes_are_capped_so_a_fork_bomb_hits_a_wall()
    {
        Assert.Contains("--pids-limit=64", Args(new SandboxLimits(MaxProcesses: 64)), StringComparison.Ordinal);
    }

    /// The container inherits **nothing** from the host process. If our API key ever appeared on this
    /// command line, a `docker inspect` would hand it to anyone — so the environment is allow-listed,
    /// never inherited.
    [Fact]
    public void No_host_environment_leaks_into_the_container()
    {
        var args = Args();

        // Only the three we explicitly set.
        var envCount = args.Split("--env=", StringSplitOptions.None).Length - 1;

        Assert.Equal(3, envCount);

        foreach (var forbidden in new[] { "ANTHROPIC", "API_KEY", "SECRET", "TOKEN", "PASSWORD" })
        {
            Assert.DoesNotContain(forbidden, args, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// **The container gets a name we chose, and the learner supplies no part of the command line.**
    /// The argv is a list, so there is no shell for a filename like `; rm -rf /` to break out into.
    [Fact]
    public void The_command_is_argv_and_not_a_shell_string()
    {
        var args = Args();

        Assert.Contains("--name=praxis-test", args, StringComparison.Ordinal);

        // `sh -c` nowhere: the command is passed as tokens, not handed to a shell.
        Assert.DoesNotContain("sh -c", args, StringComparison.Ordinal);
        Assert.DoesNotContain("/bin/sh", args, StringComparison.Ordinal);
    }

    /// The image is **ours**, pinned, and never taken from the learner's submission.
    [Fact]
    public void The_image_is_fixed_and_not_learner_chosen()
    {
        Assert.Contains("python:3.13-alpine", Args(), StringComparison.Ordinal);
    }

    /// **A skip is not a pass**, and the reason says so in as many words — so a green run of *only*
    /// skips on the escape suite cannot be mistaken for a sandbox that was verified.
    [Fact]
    public void The_docker_skip_reason_refuses_to_be_read_as_a_pass()
    {
        Assert.Contains("NOT a pass", DockerAvailable.Reason, StringComparison.Ordinal);
        Assert.Contains("has not been shown to be contained", DockerAvailable.Reason, StringComparison.Ordinal);
    }
}
