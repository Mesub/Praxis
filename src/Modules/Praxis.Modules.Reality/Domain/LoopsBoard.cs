namespace Praxis.Modules.Reality.Domain;

/// **Every prediction you have made, on the record, that you cannot take back.**
///
/// The Reality Loop's epistemic value comes entirely from *irrevocable commitment*: you say what will
/// happen, you say how sure you are, and then — win or lose — it stands. The board is where that lives.
/// It shows your open loops, their horizons, and what resolves today, and it is built so that **the
/// one thing you cannot do is make an uncomfortable prediction quietly disappear.**
///
/// ## Three things it refuses
///
/// **You cannot un-commit.** There is no "withdraw", no "hide", no filter that drops a loop you have
/// come to regret. Every open loop is on the board until reality closes it. A `Loop` is immutable and
/// `Build` includes all of them — the epistemic hygiene is structural, not a matter of the UI
/// choosing to be honest.
///
/// **An overdue loop is a debt shown, not a row deleted.** A prediction whose horizon has passed but
/// which has not resolved is a *visible obligation* — resolve it or it forfeits. A board that quietly
/// dropped stale loops would let a learner duck exactly the predictions they least want to face.
///
/// **You are measured on Coverage, which is the anti-cherry-picking metric.** `resolved / (resolved +
/// forfeited)`. You are not punished for having a life — a forfeit's *scoring* penalty is small
/// (M9.7) — but ducking resolutions is visible here, because a learner who only ever resolves the
/// ones they are winning has learned nothing about calibration and the number says so.
///
/// Pure: given the same loops and the same instant, the same board. It reads the clock nowhere —
/// `now` is passed in, because a projection that reads the wall clock is not replayable.
public static class LoopsBoard
{
    public static LoopsBoardView Build(IReadOnlyList<Loop> loops, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(loops);

        var open = loops
            .Where(l => l.Status is LoopStatus.Open)
            // **Soonest horizon first** — what needs your attention is what is about to resolve. Ties
            // broken by id so the board is deterministic.
            .OrderBy(l => l.ResolvesAt)
            .ThenBy(l => l.LoopId)
            .Select(l => Present(l, now))
            .ToList();

        var resolvedCount = loops.Count(l => l.Status is LoopStatus.Resolved);
        var forfeitedCount = loops.Count(l => l.Status is LoopStatus.Forfeited);

        var closed = resolvedCount + forfeitedCount;

        return new LoopsBoardView(
            Open: open,

            // **What resolves today** — the actionable slice.
            ResolvingToday: [.. open.Where(l => l.ResolvesToday)],

            // **The debt.** Open loops whose horizon has already passed — awaiting resolution or about
            // to forfeit. Surfaced, never hidden.
            Overdue: [.. open.Where(l => l.IsOverdue)],

            Resolved: resolvedCount,
            Forfeited: forfeitedCount,

            // Null until something has closed — reporting a coverage of "100%" off zero loops would be
            // the same overconfidence-from-no-data the diagnostic screen refuses (M7.3).
            Coverage: closed == 0 ? null : resolvedCount / (double)closed);
    }

    private static OpenLoopView Present(Loop loop, DateTime now)
        => new(
            LoopId: loop.LoopId,
            Claim: loop.Claim,
            Confidence: loop.Confidence,
            ResolvesAt: loop.ResolvesAt,

            // Whole days until it resolves; negative once it is overdue. `0` means today.
            HorizonDays: (int)Math.Floor((loop.ResolvesAt.Date - now.Date).TotalDays),

            ResolvesToday: loop.ResolvesAt.Date == now.Date,

            // **Past its horizon and still open.** A resolution that is late is a debt, not a
            // disappearance.
            IsOverdue: loop.ResolvesAt < now && loop.ResolvesAt.Date != now.Date);
}

/// One prediction Loop, as the board reads it — reconstructed from `PredictionMade` /
/// `PredictionResolved` on the event log.
public sealed record Loop(
    Guid LoopId,
    string Claim,
    double Confidence,
    DateTime OpenedAt,

    /// When the world is expected to settle this. For a code Loop, seconds from now; for a market or
    /// a PR, days.
    DateTime ResolvesAt,

    LoopStatus Status)
{
    /// Set once resolved. Null while open or forfeited — **there is no way to write these on an open
    /// loop**, which is the immutability that makes the commitment mean something.
    public bool? Correct { get; init; }

    public double? BrierScore { get; init; }
}

public enum LoopStatus
{
    /// Committed, and reality has not answered. It is on the board and it cannot be taken back.
    Open = 1,

    /// Reality answered.
    Resolved = 2,

    /// Its horizon passed without a resolution. **Not deleted** — scored slightly worse than doing
    /// nothing (M9.7), and counted against Coverage forever.
    Forfeited = 3,
}

/// **The board.**
public sealed record LoopsBoardView(
    IReadOnlyList<OpenLoopView> Open,
    IReadOnlyList<OpenLoopView> ResolvingToday,
    IReadOnlyList<OpenLoopView> Overdue,
    int Resolved,
    int Forfeited,

    /// `resolved / (resolved + forfeited)` — the fraction of your commitments you actually let the
    /// world judge. Null until something has closed.
    double? Coverage)
{
    /// The one sentence a learner reads at the top. It counts the commitments they cannot take back,
    /// and it names the debt rather than hiding it.
    public string Headline => (Open.Count, Overdue.Count, ResolvingToday.Count) switch
    {
        (0, _, _) =>
            "No open predictions. When you make one, it goes on this board and stays there until "
            + "reality settles it — that is the whole point, and it is why it works.",

        (_, > 0, _) =>
            $"{Overdue.Count} of your open predictions are past their horizon. Resolve them — a "
            + "prediction you never let resolve is not caution, it is ducking the scoreboard, and it "
            + "counts against your coverage.",

        (_, 0, > 0) =>
            $"{ResolvingToday.Count} of your {Open.Count} open predictions resolve today. This is "
            + "where you find out how well you know what you know.",

        _ => $"{Open.Count} open prediction(s) on the board, none due yet. They are on the record, and "
             + "you cannot take them back — which is exactly what makes getting them right mean "
             + "something.",
    };
}

/// An open loop, with its horizon computed against now.
public sealed record OpenLoopView(
    Guid LoopId,
    string Claim,
    double Confidence,
    DateTime ResolvesAt,

    /// Whole days until resolution; `0` is today, negative is overdue.
    int HorizonDays,

    bool ResolvesToday,
    bool IsOverdue);
