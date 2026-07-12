using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;

namespace Praxis.Modules.Reality.Tests;

/// **The hostile-code suite.**
///
/// Every test in this file is a thing a real attacker actually tries, run as real code inside the real
/// sandbox. The comfortable version of these tests asserts the *flags are present*; this version
/// asserts the *attacks fail*, because a flag that is present and does not work is worse than one that
/// is absent — it looks like a defence.
///
/// **Learner-submitted code is malware.** These tests are PRAXIS acting as the malware.
[Collection("sandbox")]
public sealed class SandboxEscapeTests
{
    private static DockerSandbox Sandbox() => new(NullLogger<DockerSandbox>.Instance);

    private static SandboxJob Attack(string python, SandboxLimits? limits = null)
        => new(
            Files: new Dictionary<string, string> { ["attack.py"] = python },
            Command: ["python", "attack.py"],
            Limits: limits ?? SandboxLimits.Default);

    // ── The one that matters most: no network ────────────────────────────────

    /// **There is no network, and this proves it by trying to use one.**
    ///
    /// The hidden tests are the entire basis of BUILD evidence. A container that can reach the
    /// internet can fetch the reference solution, exfiltrate the tests it was handed, or send somebody
    /// else's data somewhere. So the attack — open a socket to a public DNS server — must **fail**, and
    /// fail because the interface is *absent*, not because a filter said no.
    [RequiresDocker]
    public async Task Code_cannot_reach_the_network()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            import socket
            socket.setdefaulttimeout(5)
            socket.create_connection(("1.1.1.1", 53))
            print("NETWORK REACHED")
            """));

        Assert.True(result.IsSuccess, result.Message);

        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
        Assert.DoesNotContain("NETWORK REACHED", result.Value.Stdout, StringComparison.Ordinal);
    }

    /// A DNS lookup is a network call too, and a subtler one. **Also absent.**
    [RequiresDocker]
    public async Task Code_cannot_resolve_a_hostname()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            import socket
            print(socket.gethostbyname("example.com"))
            """));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
    }

    // ── The filesystem is read-only ──────────────────────────────────────────

    /// **A submission that rewrites its own test file to assert it passed finds that it cannot.**
    ///
    /// This is the attack the read-only mount exists for: the tests arrive on the same mount as the
    /// code, and if the code could edit them, BUILD evidence would be forgeable by a `sed` command.
    [RequiresDocker]
    public async Task Code_cannot_write_to_its_own_workspace()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            with open("hidden_tests.py", "w") as f:
                f.write("def test_all(): pass")
            print("REWROTE THE TESTS")
            """));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
        Assert.DoesNotContain("REWROTE THE TESTS", result.Value.Stdout, StringComparison.Ordinal);
    }

    /// The rest of the container's filesystem is read-only too. `/etc` is not ours to let them touch.
    [RequiresDocker]
    public async Task Code_cannot_write_outside_the_scratch_mount()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            with open("/etc/praxis_pwned", "w") as f:
                f.write("x")
            print("WROTE TO ETC")
            """));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
    }

    // ── The walls hold ───────────────────────────────────────────────────────

    /// **An infinite loop is a bug, not a mystery.** It hits the wall clock and comes back as the
    /// learner's outcome — `TimedOut` — rather than hanging the host forever.
    [RequiresDocker]
    public async Task An_infinite_loop_is_stopped_by_the_wall_clock()
    {
        var result = await Sandbox().RunAsync(
            Attack("while True: pass", new SandboxLimits(WallClockSeconds: 3)));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionOutcome.TimedOut, result.Value.Outcome);

        // And it did not take much longer than its wall to die.
        Assert.True(result.Value.DurationMs < 15_000);
    }

    /// **A fork bomb hits the pids limit, not the host.**
    [RequiresDocker]
    public async Task A_fork_bomb_cannot_take_down_the_host()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            import os
            while True:
                os.fork()
            """,
            new SandboxLimits(WallClockSeconds: 5, MaxProcesses: 16)));

        // It does not matter *how* it fails — timed out, crashed, killed. It matters that we got a
        // result back at all, which means the host is still standing.
        Assert.True(result.IsSuccess, "The host did not survive a fork bomb.");
        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
    }

    /// **A program that tries to allocate the world comes back as `OutOfMemory`**, a fact about the
    /// code — not as a host that fell over.
    [RequiresDocker]
    public async Task Runaway_allocation_is_capped()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            x = bytearray()
            while True:
                x.extend(bytearray(10_000_000))
            """,
            new SandboxLimits(MemoryMb: 128, WallClockSeconds: 8)));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(ExecutionOutcome.Passed, result.Value.Outcome);
    }

    /// **A program that prints forever will exhaust the machine collecting it.** The output is capped,
    /// the truncation is recorded honestly, and the host does not run out of memory buffering it.
    [RequiresDocker]
    public async Task Unbounded_output_is_truncated_rather_than_collected_forever()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            while True:
                print("A" * 1000)
            """,
            new SandboxLimits(WallClockSeconds: 5, MaxOutputBytes: 32 * 1024)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.OutputTruncated, "A program printing forever was not truncated.");
        Assert.True(
            result.Value.Stdout.Length <= 64 * 1024,
            $"Collected {result.Value.Stdout.Length} bytes despite a 32 KB cap.");
    }

    // ── And an honest program is graded honestly ─────────────────────────────

    /// **The point of all of the above.** A learner who wrote correct code gets `Passed`, promptly,
    /// with their output — the walls are invisible to somebody who was not attacking them.
    [RequiresDocker]
    public async Task Correct_code_passes_cleanly()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            def add(a, b):
                return a + b

            assert add(2, 3) == 5
            print("ok")
            """,
            new SandboxLimits(WallClockSeconds: 30)));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionOutcome.Passed, result.Value.Outcome);
        Assert.Equal(0, result.Value.ExitCode);
        Assert.Contains("ok", result.Value.Stdout, StringComparison.Ordinal);
    }

    /// **Wrong code fails honestly** — `Failed`, with the traceback on stderr, and no LLM anywhere in
    /// the verdict. It either ran and passed or it did not (C-4).
    [RequiresDocker]
    public async Task Wrong_code_fails_with_its_own_error()
    {
        var result = await Sandbox().RunAsync(Attack(
            """
            assert 2 + 2 == 5, "maths still works"
            """,
            new SandboxLimits(WallClockSeconds: 30)));

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutionOutcome.Failed, result.Value.Outcome);
        Assert.NotEqual(0, result.Value.ExitCode);
        Assert.Contains("maths still works", result.Value.Stderr, StringComparison.Ordinal);
    }
}

[CollectionDefinition("sandbox", DisableParallelization = true)]
public sealed class SandboxTests;
