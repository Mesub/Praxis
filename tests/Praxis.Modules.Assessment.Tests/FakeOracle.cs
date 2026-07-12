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

    public List<CheckClaimRequest> Asked { get; } = [];

    public void Says(ClaimOutcome outcome, string? misconception = null)
    {
        _outcome = outcome;
        _misconception = misconception;
        _unreachable = false;
    }

    /// The oracle is down. **PRAXIS must not guess, and must not default to WRONG.**
    public void Fails() => _unreachable = true;

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

    public Task<Result<GradeResponse>> GradeAsync(GradeRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Not part of this slice.");

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
