using Praxis.Infrastructure.Ai;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **The oracle, stubbed — and the stub is the point.**
///
/// This class implements the *whole* `IAiPlane` contract, which means it is bound by the same shape
/// the real one is: `CheckClaimResponse` has **three words and an optional key**, and nowhere to put
/// an answer.
///
/// So even a *deliberately malicious* fake — one written by somebody actively trying to leak the
/// answer into the core — could not do it. There is no field. That is what it means for a guarantee
/// to be structural rather than a matter of good behaviour: it holds **against the test doubles too**.
public sealed class FakeOracle : IAiPlane
{
    private ClaimOutcome _outcome = ClaimOutcome.Correct;
    private string? _misconception;
    private bool _unreachable;

    private GradeResponse? _grade;
    private bool _graderDown;

    public List<CheckClaimRequest> Asked { get; } = [];

    /// The explanations handed to the grader — captured so a test can prove the speech was stripped
    /// *before* the rubric ever saw the words.
    public List<GradeByConceptRequest> Graded { get; } = [];

    public void Says(ClaimOutcome outcome, string? misconception = null)
    {
        _outcome = outcome;
        _misconception = misconception;
        _unreachable = false;
    }

    /// The oracle is down. **PRAXIS must not guess, and must not default to WRONG.**
    public void Fails() => _unreachable = true;

    /// Script the grader's verdict on the next teach-back.
    public void Grades(GradeResponse grade)
    {
        _grade = grade;
        _graderDown = false;
    }

    /// The grader is down. **PRAXIS must not invent a score.**
    public void GraderFails() => _graderDown = true;

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Asked.Add(request);

        return Task.FromResult(_unreachable
            ? Result<CheckClaimResponse>.Invalid("The oracle could not be reached.")
            : Result<CheckClaimResponse>.Success(
                new CheckClaimResponse(_outcome, _misconception, Usd: 0.0004)));
    }

    public Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Graded.Add(request);

        if (_graderDown)
        {
            return Task.FromResult(Result<GradeResponse>.Invalid("The grader could not be reached."));
        }

        return Task.FromResult(Result<GradeResponse>.Success(
            _grade ?? new GradeResponse(GradeOutcome.Passed, 0.9, [], "stub", 1, false, false, 0.0)));
    }

    public Task<Result<ItemResponse>> GenerateItemAsync(
        GenerateItemRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

    public Task<Result<TutorTurnResponse>> TutorTurnAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

    public IAsyncEnumerable<string> TutorTurnStreamAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");
}
