using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **Learner-submitted code is malware.**
///
/// Not *"might be"*. Not *"could theoretically be"*. **Is.** That is the only assumption under which
/// it is safe to run somebody else's program on your infrastructure, and every line of this file
/// follows from it.
///
/// A learner submitting a `BUILD` is handing PRAXIS an executable and asking it to run it. Most of
/// them are trying to implement a rate limiter. One of them is trying to read `/etc/shadow`, one is
/// trying to `curl` our hidden test file out of the container, one is mining Monero, and one is
/// forking until the host falls over. **The code cannot tell you which one it is**, and the sandbox
/// is what makes the question irrelevant.
///
/// ## What the sandbox refuses
///
/// **There is no network. Not firewalled — absent** (`--network=none`). This is not a defence-in-depth
/// nicety, it is load-bearing: the hidden tests are the *entire* basis of `BUILD` evidence (M9.2), and
/// a container that can reach the internet can fetch the reference solution, exfiltrate the tests it
/// was given, or phone home with somebody else's data. A network *filter* is a thing that can be
/// misconfigured. An absent network interface is not.
///
/// **There are no secrets.** The container's environment is empty except for what it needs to run
/// Python. Our API keys live in the host process, and a process with no network could not use them
/// anyway — but *both* are true, because one of them is going to be wrong one day.
///
/// **There is no writable filesystem** except one small scratch mount. The code and the tests arrive
/// on a read-only mount. A program that tries to rewrite its own test file finds it cannot.
///
/// **There are no privileges**: every capability dropped, `no-new-privileges`, a non-root uid that
/// exists nowhere in our system, and a hard cap on processes so that a fork bomb hits a wall instead
/// of the host.
///
/// **And it cannot run forever, allocate forever, or print forever.** Wall clock, memory and *output*
/// are all capped — the last one because a program printing an infinite stream will exhaust the
/// machine collecting it, and that is a denial-of-service against every other learner on the box.
public interface ISandbox
{
    /// Run untrusted code and come back with **what happened**, not with an opinion about it.
    ///
    /// A `Result` failure here means **the sandbox itself failed** — Docker was down, the image was
    /// missing, the host was out of disk. It does **not** mean the learner's code failed, and the
    /// distinction is the most important thing in this interface (see `ExecutionResult`).
    Task<Result<ExecutionResult>> RunAsync(SandboxJob job, CancellationToken ct = default);
}

/// A program, its tests, and the walls it will run inside.
public sealed record SandboxJob(
    /// Files to place on the read-only mount, by relative path. The learner's code and the hidden
    /// tests arrive the same way and are equally unwritable.
    IReadOnlyDictionary<string, string> Files,

    /// What to execute inside the container. **Not a shell string** — an argv list, so there is no
    /// shell to inject into.
    IReadOnlyList<string> Command,

    SandboxLimits Limits)
{
    /// The image. Pinned, minimal, and **ours to choose** — never the learner's.
    public string Image { get; init; } = "python:3.13-alpine";

    /// **Host-controlled environment only.** Nothing here ever comes from the learner — it is how the
    /// grading harness receives the per-run nonce (`WithNonce`), and a learner-supplied value must
    /// never reach it, because an env var is a channel and every channel into untrusted code is a
    /// channel out of it.
    public IReadOnlyDictionary<string, string> Env { get; init; } =
        new Dictionary<string, string>();

    /// Hand the container a single host-generated environment value.
    public SandboxJob WithNonce(string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        return this with
        {
            Env = new Dictionary<string, string>(Env) { ["PRAXIS_NONCE"] = nonce },
        };
    }
}

/// **The walls.**
///
/// Every one of these is a number somebody will eventually want to raise "just for this one case".
/// The defaults are deliberately mean: a learner's rate limiter does not need half a gigabyte, and an
/// implementation that needs sixty seconds to pass six tests has told you something true about itself.
public sealed record SandboxLimits(
    int MemoryMb = 256,
    double Cpus = 0.5,

    /// **Wall clock, not CPU time.** A program that sleeps for an hour has still occupied a slot for
    /// an hour.
    int WallClockSeconds = 10,

    /// A fork bomb hits this, not the host.
    int MaxProcesses = 64,

    /// **A program that prints forever will exhaust the machine collecting it.** That is a
    /// denial-of-service against every other learner on the box, and it costs one integer to prevent.
    int MaxOutputBytes = 64 * 1024)
{
    public static SandboxLimits Default { get; } = new();
}

/// **What happened.** Not what it means.
///
/// There is no `Score` here, no `Feedback`, and no `Explanation`. The sandbox reports an exit code and
/// what came out of the pipes. **Whether that constitutes mastery is not its business**, and the
/// moment it acquires an opinion it becomes a thing that can be argued with.
public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    int ExitCode,
    string Stdout,
    string Stderr,
    int DurationMs)
{
    /// **The only thing that counts as passing.** Exit zero, under the walls.
    ///
    /// Note what cannot happen: there is no path from `TimedOut` or `OutOfMemory` to `Passed`, no
    /// partial credit, and nothing an LLM can say about the stderr that changes it. `BUILD` evidence
    /// is the one kind PRAXIS cannot be talked into (C-4) — **it either ran or it did not.**
    public bool Passed => Outcome is ExecutionOutcome.Passed;

    /// The output was cut off at the cap. Recorded honestly, because a truncated stack trace that
    /// *looks* complete is worse than one that says it was truncated.
    public bool OutputTruncated { get; init; }
}

/// **Every one of these is a fact about the learner's code.**
///
/// There is deliberately no member for *"the sandbox broke"*. That is not an outcome, it is an absence
/// of one — and if it were in this enum, somebody would eventually write
/// `outcome != Passed → evidence of failure`, and a learner would be marked down because our Docker
/// daemon was restarting. It comes back as a `Result` failure instead, so it **cannot** be mistaken
/// for a verdict.
public enum ExecutionOutcome
{
    /// It ran, and the hidden tests were satisfied.
    Passed = 1,

    /// It ran, and they were not. **This is a real, honest answer** — the most common one, and the
    /// one the whole engine exists to produce.
    Failed = 2,

    /// It did not finish inside the wall clock. **This is the learner's outcome, not ours**: an
    /// implementation that cannot answer in ten seconds has said something true about itself, and an
    /// infinite loop is a bug rather than a mystery.
    TimedOut = 3,

    /// It tried to allocate more than it was given. Also a fact about the code.
    OutOfMemory = 4,
}
