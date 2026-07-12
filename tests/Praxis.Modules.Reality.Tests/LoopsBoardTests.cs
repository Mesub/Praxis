using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Tests;

/// **The board where your predictions live and cannot be taken back.**
///
/// These check the three things that make it more than a list: it shows every open commitment, it
/// surfaces the overdue ones as a debt rather than dropping them, and it measures the fraction you let
/// resolve.
public sealed class LoopsBoardTests
{
    private static readonly DateTime Now = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Loop Open(string claim, DateTime resolvesAt, double confidence = 0.7)
        => new(Guid.CreateVersion7(), claim, confidence, Now.AddDays(-1), resolvesAt, LoopStatus.Open);

    private static Loop Resolved(bool correct)
        => new(Guid.CreateVersion7(), "resolved", 0.7, Now.AddDays(-5), Now.AddDays(-4), LoopStatus.Resolved)
        {
            Correct = correct,
            BrierScore = correct ? 0.09 : 0.49,
        };

    private static Loop Forfeited()
        => new(Guid.CreateVersion7(), "forfeited", 0.7, Now.AddDays(-10), Now.AddDays(-3), LoopStatus.Forfeited);

    // ── You cannot un-commit ─────────────────────────────────────────────────

    /// **Every open loop is on the board.** There is no filter, no "hide", no way to drop the one you
    /// have come to regret. This is the epistemic backbone — a prediction you made is a prediction you
    /// face.
    [Fact]
    public void Every_open_loop_appears_on_the_board()
    {
        var loops = new[]
        {
            Open("A will print 42", Now.AddDays(2)),
            Open("B raises ValueError", Now.AddDays(5)),
            Open("C is O(n log n)", Now.AddDays(1)),
        };

        var board = LoopsBoard.Build(loops, Now);

        Assert.Equal(3, board.Open.Count);
        Assert.Equal(
            loops.Select(l => l.LoopId).OrderBy(x => x),
            board.Open.Select(l => l.LoopId).OrderBy(x => x));
    }

    /// **There is no path to write an outcome onto an open loop.** `Correct` and `BrierScore` are
    /// init-only and null on an open loop — the commitment cannot be edited into having been right.
    [Fact]
    public void An_open_loop_has_no_outcome_and_no_way_to_set_one()
    {
        var loop = Open("x", Now.AddDays(1));

        Assert.Null(loop.Correct);
        Assert.Null(loop.BrierScore);

        var settable = typeof(Loop).GetProperties()
            .Where(p => p.SetMethod?.IsPublic == true)
            .Where(p => p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
                .All(m => m.Name != "IsExternalInit"))
            .ToList();

        Assert.Empty(settable);
    }

    // ── Horizon ──────────────────────────────────────────────────────────────

    /// **Soonest first.** The board leads with what is about to resolve, because that is what needs
    /// attention.
    [Fact]
    public void Open_loops_are_ordered_by_horizon_soonest_first()
    {
        var board = LoopsBoard.Build(
            [
                Open("far", Now.AddDays(10)),
                Open("soon", Now.AddDays(1)),
                Open("middle", Now.AddDays(4)),
            ],
            Now);

        Assert.Equal(["soon", "middle", "far"], board.Open.Select(l => l.Claim));
    }

    /// **What resolves today is the actionable slice.**
    [Fact]
    public void The_board_singles_out_what_resolves_today()
    {
        var board = LoopsBoard.Build(
            [
                Open("today", Now.AddHours(6)),
                Open("tomorrow", Now.AddDays(1)),
                Open("also today", Now.AddHours(2)),
            ],
            Now);

        Assert.Equal(2, board.ResolvingToday.Count);
        Assert.All(board.ResolvingToday, l => Assert.True(l.ResolvesToday));
        Assert.Contains(board.ResolvingToday, l => l.Claim == "today");
        Assert.Contains("resolve today", board.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Horizon_days_count_whole_days_to_resolution()
    {
        var board = LoopsBoard.Build([Open("in three days", Now.AddDays(3))], Now);

        Assert.Equal(3, board.Open[0].HorizonDays);
    }

    // ── Overdue is a debt shown, not a row deleted ───────────────────────────

    /// **An overdue loop is surfaced, not dropped.** A prediction whose horizon has passed is a
    /// visible obligation — resolve it or forfeit it. A board that hid it would let a learner duck the
    /// predictions they least want to face.
    [Fact]
    public void An_overdue_open_loop_is_surfaced_as_a_debt()
    {
        var board = LoopsBoard.Build(
            [
                Open("well past due", Now.AddDays(-3)),
                Open("fine", Now.AddDays(2)),
            ],
            Now);

        // Still on the board...
        Assert.Equal(2, board.Open.Count);

        // ...and called out.
        var overdue = Assert.Single(board.Overdue);
        Assert.Equal("well past due", overdue.Claim);
        Assert.True(overdue.IsOverdue);
        Assert.True(overdue.HorizonDays < 0);

        Assert.Contains("past their horizon", board.Headline, StringComparison.Ordinal);
        Assert.Contains("ducking the scoreboard", board.Headline, StringComparison.Ordinal);
    }

    // ── Coverage: the anti-cherry-picking metric ─────────────────────────────

    /// **Coverage counts the fraction you let the world judge.** A learner who resolves the wins and
    /// forfeits the rest has a coverage that says so — you cannot cherry-pick your track record.
    [Fact]
    public void Coverage_is_resolved_over_resolved_plus_forfeited()
    {
        var board = LoopsBoard.Build(
            [
                Resolved(correct: true),
                Resolved(correct: false),
                Resolved(correct: true),
                Forfeited(),
            ],
            Now);

        // 3 resolved of 4 closed → 0.75.
        Assert.Equal(0.75, board.Coverage!.Value, tolerance: 1e-9);
        Assert.Equal(3, board.Resolved);
        Assert.Equal(1, board.Forfeited);
    }

    /// **A forfeit is not a deletion.** It stays counted against coverage — you were not punished for
    /// having a life, but you do not get to pretend the prediction never happened.
    [Fact]
    public void Forfeiting_lowers_coverage_rather_than_erasing_the_loop()
    {
        var clean = LoopsBoard.Build([Resolved(true), Resolved(true)], Now);
        var ducked = LoopsBoard.Build([Resolved(true), Resolved(true), Forfeited(), Forfeited()], Now);

        Assert.Equal(1.0, clean.Coverage!.Value, tolerance: 1e-9);
        Assert.Equal(0.5, ducked.Coverage!.Value, tolerance: 1e-9);
    }

    /// **No coverage claimed off zero closed loops** — the same no-verdict-on-no-data rule as the
    /// diagnostic screen (M7.3). Reporting "100%" here would be overconfidence from nothing.
    [Fact]
    public void Coverage_is_null_until_something_has_closed()
    {
        var board = LoopsBoard.Build([Open("still open", Now.AddDays(1))], Now);

        Assert.Null(board.Coverage);
    }

    // ── Determinism & empty ──────────────────────────────────────────────────

    [Fact]
    public void The_board_is_deterministic()
    {
        var loops = new[]
        {
            Open("a", Now.AddDays(2)),
            Open("b", Now.AddDays(2)),   // same horizon — tie broken by id
            Open("c", Now.AddDays(1)),
        };

        var first = LoopsBoard.Build(loops, Now);
        var second = LoopsBoard.Build(loops, Now);

        Assert.Equal(first.Open.Select(l => l.LoopId), second.Open.Select(l => l.LoopId));
    }

    [Fact]
    public void An_empty_board_says_what_it_is_for()
    {
        var board = LoopsBoard.Build([], Now);

        Assert.Empty(board.Open);
        Assert.Null(board.Coverage);
        Assert.Contains("goes on this board and stays there", board.Headline, StringComparison.Ordinal);
    }
}
