using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Modules.Reality.Domain;
using Praxis.Modules.Reality.Infrastructure;

namespace Praxis.Modules.Reality.Tests;

/// **The whole Reality Loop, composed, running for real.**
///
/// The unit suites prove each piece; this proves they fit together — a learner commits a prediction
/// (M9.3), reality resolves it in the sandbox (M9.1), the outcome is decoupled from the rationale
/// (M9.5), and the closed loop shows up on the board with an honest Coverage (M9.6). If the seams
/// between these were wrong, every unit test could still pass and the product still not work; this is
/// the test that would catch that.
[Collection("sandbox")]
public sealed class RealityLoopEndToEndTests
{
    private static BehaviourResolver Resolver()
        => new(new DockerSandbox(NullLogger<DockerSandbox>.Instance));

    /// **Right, and knew why — the full happy path.** Predict the exception, name it, be right, and
    /// have a rationale that holds: mastery, a clean Ledger score, and a resolved loop on the board.
    [RequiresDocker]
    public async Task A_correct_prediction_with_sound_reasoning_flows_all_the_way_to_the_board()
    {
        // 1. Commit — an answer and a confidence, before the code runs (M9.3).
        var question = new PredictionQuestion(PredictionKind.Raises, "print(1 / 0)");
        var committed = BehaviourPrediction.Commit(question, "raises ZeroDivisionError", confidence: 0.85).Value;

        // 2. Reality resolves it, in the sandbox (M9.1 + M9.3).
        var resolution = (await Resolver().ResolveAsync(question, committed)).Value;
        Assert.True(resolution.Correct);

        // 3. Decouple outcome from process (M9.5). The reasoning was sound.
        var outcome = LoopOutcome.From(resolution);
        var verdict = LoopScoring.Score(outcome, new ThesisScore(0.9, []));

        Assert.Equal(LoopQuadrant.RightAndKnewWhy, verdict.Quadrant);
        Assert.NotNull(verdict.LedgerScore);
        Assert.Equal(1.0 - resolution.BrierScore, verdict.LedgerScore!.Value, tolerance: 1e-9);

        // 4. The closed loop on the board, with coverage (M9.6).
        var loopId = Guid.CreateVersion7();
        var now = new DateTime(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

        var board = LoopsBoard.Build(
            [
                new Loop(loopId, "raises ZeroDivisionError", 0.85, now.AddMinutes(-1), now.AddMinutes(-1),
                    LoopStatus.Resolved)
                {
                    Correct = resolution.Correct,
                    BrierScore = resolution.BrierScore,
                },
            ],
            now);

        Assert.Equal(1, board.Resolved);
        Assert.Equal(1.0, board.Coverage!.Value, tolerance: 1e-9);
        Assert.Empty(board.Open);
    }

    /// **Right for the wrong reason — the dangerous path, caught.** The learner predicts *some* crash
    /// and gets lucky that it was the one they named, but their rationale did not engage the mechanism.
    /// Reality confirms the outcome; the decoupling flags the process; the Ledger still records the
    /// verified result and nothing more.
    [RequiresDocker]
    public async Task A_correct_prediction_with_weak_reasoning_is_flagged_but_still_counts()
    {
        var question = new PredictionQuestion(PredictionKind.Raises, "[][5]");
        var committed = BehaviourPrediction.Commit(question, "raises IndexError", confidence: 0.6).Value;

        var resolution = (await Resolver().ResolveAsync(question, committed)).Value;
        Assert.True(resolution.Correct);

        var verdict = LoopScoring.Score(
            LoopOutcome.From(resolution),
            new ThesisScore(0.25, ["bounds_checking"]));

        // The result counts — reality said they were right...
        Assert.Equal(1.0 - resolution.BrierScore, verdict.LedgerScore!.Value, tolerance: 1e-9);

        // ...but it is named as luck, not understanding.
        Assert.Equal(LoopQuadrant.RightForTheWrongReason, verdict.Quadrant);
        Assert.Contains("bounds_checking", verdict.Debrief, StringComparison.Ordinal);
    }

    /// **Wrong, and it feeds calibration honestly.** A confident wrong prediction resolves, and the
    /// Brier flows into the calibration profile as the expensive miss it is.
    [RequiresDocker]
    public async Task A_confident_wrong_prediction_lands_in_the_calibration_profile()
    {
        var question = new PredictionQuestion(PredictionKind.Output, "print('actually this')");
        var committed = BehaviourPrediction.Commit(question, "something else", confidence: 0.95).Value;

        var resolution = (await Resolver().ResolveAsync(question, committed)).Value;
        Assert.False(resolution.Correct);

        // The single resolution, folded into a profile alongside a few others, reads as overconfident.
        var profile = Calibration.FromResolutions(
        [
            resolution,
            resolution, resolution, resolution, resolution,
            resolution, resolution, resolution,
        ]);

        Assert.True(profile.HasVerdict);
        Assert.True(profile.Overconfidence > 0.5);
        Assert.Contains("overconfident", profile.Headline, StringComparison.Ordinal);
    }
}
