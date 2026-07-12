using Praxis.Shared;

namespace Praxis.Infrastructure.Ai;

/// **The seam between two languages.**
///
/// These records mirror `contracts/ai-plane.openapi.json` exactly, and `AiPlaneContractTests`
/// fails the build if they ever stop doing so. That test is the point of this file — a hand-written
/// DTO that has quietly drifted from the service it talks to is worse than no DTO, because both
/// sides believe they agree and the disagreement surfaces as a null in a learner's grade.
///
/// **A note on "generated".** The roadmap says *OpenAPI → generated C# client*. It is not generated;
/// it is written, and then *checked* against the contract by a test that reads the JSON. The
/// guarantee is the same one codegen offers (the two sides cannot silently diverge) and I preferred
/// it because generated clients are unreviewable — and every guarantee in this system needs a
/// reviewer to be able to read it and see that it holds. The drift test is short enough to audit;
/// an NSwag output is not.
///
/// ## What is not here
///
/// There is no `answer`. There is no `GetAnswerAsync`. There is no debug endpoint, no admin route,
/// and no `includeAnswer` flag — not on the wire, not on these types, and not on the client. The
/// .NET core is a program with an HTTP client, and it can call anything this contract exposes: if an
/// answer were reachable from here, the tutor could reach it, and PRAXIS's central claim would be
/// marketing (C-5).
public interface IAiPlane
{
    /// **The oracle.** The tutor's only way to find out whether the learner is right.
    ///
    /// It gets back one of three words and possibly a misconception key. It does not get back the
    /// answer, because there is no field for one.
    Task<Result<CheckClaimResponse>> CheckClaimAsync(CheckClaimRequest request, CancellationToken ct = default);

    /// Grade an open response. **May return `Pending`, and the caller must handle it as a real
    /// outcome** — it is not a slow `Passed` (R-1).
    Task<Result<GradeResponse>> GradeAsync(GradeRequest request, CancellationToken ct = default);

    /// A never-seen item. The answer key goes to the oracle's store on the far side and is never
    /// returned here.
    Task<Result<ItemResponse>> GenerateItemAsync(GenerateItemRequest request, CancellationToken ct = default);
}

// ── The oracle ───────────────────────────────────────────────────────────────

public sealed record CheckClaimRequest(
    string ItemId,
    string Claim,

    /// Required on every call: the AI plane is stateless and cannot know who is asking, and nothing
    /// is spent anonymously.
    string LearnerId,
    string ConceptId);

/// **Everything the tutor is ever told.** Three words, and possibly a key.
public sealed record CheckClaimResponse(
    ClaimOutcome Outcome,
    string? Misconception,
    double? Usd);

public enum ClaimOutcome
{
    Correct = 1,
    Partial = 2,
    Wrong = 3,
}

// ── Grading ──────────────────────────────────────────────────────────────────

public sealed record GradeRequest(
    string Response,
    RubricSpec Rubric,
    string LearnerId,
    string ConceptId);

/// The rubric travels *to* the grader on every call, because the AI plane remembers nothing.
///
/// It is answer-adjacent — a band description is, in practice, the answer — which is exactly why it
/// goes to the grader and never to the tutor.
public sealed record RubricSpec(
    string Id,
    string ConceptId,
    string Audience,
    IReadOnlyList<DimensionSpec> Dimensions,
    double PassScore = 0.75,
    int MinLength = 40,
    IReadOnlyList<HardFailSpec>? HardFails = null);

public sealed record DimensionSpec(
    string Id,
    double Weight,
    string Probe,
    IReadOnlyDictionary<int, string> Bands,
    int? AutoFlagAtOrBelow = null,
    string? AutoFlagMisconception = null);

public sealed record HardFailSpec(string Pattern, string Misconception, double Cap);

public sealed record GradeResponse(
    GradeOutcome Outcome,
    double? Score,
    IReadOnlyList<string> Misconceptions,
    string Reason,
    int JudgesConsulted,
    bool EscalatedToAdjudicator,
    bool EscalatedToHuman,
    double? Usd);

/// **`Pending` is a real outcome and it crosses the wire** (R-1).
///
/// If it were not in this enum, the core would have no way to represent *"PRAXIS does not know"* —
/// and it would have to pick one of the other two. It would pick `Passed`, on the day the queue was
/// long and it was probably fine.
public enum GradeOutcome
{
    Passed = 1,
    Failed = 2,

    /// A human decides. This evidence **cannot move mastery** until they do.
    Pending = 3,
}

// ── Items ────────────────────────────────────────────────────────────────────

public sealed record GenerateItemRequest(
    string ConceptId,
    string ConceptSummary,
    EvidenceType EvidenceType,
    double Difficulty,
    string LearnerId,

    /// Fingerprints of what this learner has already been shown. **The core remembers; the AI plane
    /// does not.** Novelty is checked against these deterministically on the far side (M5.5).
    IReadOnlyList<string> AlreadySeen);

/// **Everything the core, the tutor and the learner ever see of an item.**
///
/// No `Answer`. The answer exists — it went to the oracle's store the moment the item was written —
/// and the only thing that ever comes back out is one of three words.
public sealed record ItemResponse(
    string Id,
    string ConceptId,
    EvidenceType EvidenceType,
    string Stem,
    double Difficulty,

    /// A stable hash of the stem. The core stores it, and passes it back in `AlreadySeen` next time,
    /// which is how "never-seen" stays a fact rather than a hope.
    string Fingerprint);
