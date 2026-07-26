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

    /// Grade an open response **by naming the concept**. The rubric is *not* sent: it is
    /// answer-adjacent and lives on the AI plane (M11.7a), and the grader resolves it there. The core
    /// never holds a rubric, so there is no answer-adjacent thing here for the tutor to reach.
    ///
    /// **May return `Pending`, and the caller must handle it as a real outcome** — it is not a slow
    /// `Passed` (R-1).
    Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default);

    /// A never-seen item. The answer key goes to the oracle's store on the far side and is never
    /// returned here.
    Task<Result<ItemResponse>> GenerateItemAsync(GenerateItemRequest request, CancellationToken ct = default);

    /// **One turn of Socratic dialogue.**
    ///
    /// Look at `TutorTurnRequest`. There is no `Answer`, no `Rubric`, no `Expected`. The core could
    /// not send the tutor an answer even if it had one — and it does not have one, because the
    /// answer lives in the oracle's store on the *far* side of this wire and comes back only as one
    /// of three words.
    ///
    /// **That is the guarantee surviving the network hop**, which is where most guarantees quietly
    /// die.
    Task<Result<TutorTurnResponse>> TutorTurnAsync(TutorTurnRequest request, CancellationToken ct = default);

    /// **The same turn, a token at a time — and a stream has no schema.**
    ///
    /// Every other guarantee in PRAXIS is enforced by a *type*: the verdict has no field for a
    /// sentence, the item has no field for an answer, this contract has no field for a key. A stream
    /// has no fields **at all**. It is free text going straight to a learner's browser, and nothing
    /// downstream can inspect it.
    ///
    /// So the protection cannot come from the shape of the output. It comes from the shape of the
    /// *input*: the request below is the same `TutorTurnRequest`, with the same absent fields, and a
    /// model streaming its whole context verbatim streams no answer. That is the **only** thing
    /// standing here — which is why the jailbreak suite is re-run against the stream rather than
    /// assumed to carry over.
    IAsyncEnumerable<string> TutorTurnStreamAsync(TutorTurnRequest request, CancellationToken ct = default);
}

// ── The tutor ────────────────────────────────────────────────────────────────

/// **Everything the tutor is ever given.** No answer. No rubric. No expected.
public sealed record TutorTurnRequest(
    string ConceptSummary,

    /// The question the learner is looking at. They can see it — it is not a secret.
    string ItemStem,

    /// The conversation so far. **The learner's words live here and nowhere else** — they are data,
    /// never instructions, and a learner who types "ignore all previous instructions" has typed a
    /// sentence.
    IReadOnlyList<TurnSpec> Transcript,

    /// The deepest rung the ladder has authorised. There is nothing below `PartialScaffold`.
    HintRung Rung,

    int BudgetRemaining,

    /// Misconception **keys** only, from a closed declared set. Never free text.
    IReadOnlyList<string> KnownMisconceptions,

    string LearnerId,
    string ConceptId,

    /// The passage the learner is reading, when the item has one. **They are looking at it**, so a
    /// hint that points into it ("what does *however* in sentence two reverse?") is teaching, not
    /// answering.
    ///
    /// There is no `Options` beside this, and there must never be. The choices are a closed set
    /// containing the answer: a model that can see them can eliminate them, and elimination *is*
    /// the answer, delivered politely. The learner sees the options; the tutor does not.
    string? Passage = null);

public sealed record TurnSpec(string Who, string Said);

public sealed record TutorTurnResponse(string Text, double? Usd);

/// Mirrors `Praxis.Modules.Kernel.Events.HintRung` and the AI plane's enum, exactly.
///
/// **There is no sixth rung.** CANON lists an `L5 FULL_REVEAL` and marks it *"in LEARN mode this
/// rung does not exist"* — so it does not exist here, on the wire, or on the far side. Not gated,
/// not flagged: **absent**.
public enum HintRung
{
    Orient = 0,
    Probe = 1,
    Analogy = 2,
    WorkedExample = 3,
    PartialScaffold = 4,
}

// ── The oracle ───────────────────────────────────────────────────────────────

public sealed record CheckClaimRequest(
    string ItemId,
    string Claim,

    /// Required on every call: the AI plane is stateless and cannot know who is asking, and nothing
    /// is spent anonymously.
    string LearnerId,
    string ConceptId,

    /// The option the learner chose, for a fixed-key item — and **nothing else about it**. The
    /// index of the *correct* one is not here, is not on the response, and is not obtainable from
    /// this process: it is in `praxis_oracle`, which this connection string does not reach.
    int? SelectedOption = null);

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

/// **What crosses the wire to grade a teach-back: the words, the learner, and the concept.**
///
/// Note what is *not* here — a rubric. The rubric's bands are, in practice, the answer, so they live
/// on the AI plane (M11.7a) and the grader resolves them from the concept. The core cannot send a
/// rubric because it does not have one, which is the same reason the tutor cannot leak an answer:
/// it is absent, not withheld.
public sealed record GradeByConceptRequest(
    string Response,
    string LearnerId,
    string ConceptId);

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
    IReadOnlyList<string> AlreadySeen,

    /// The concept's declared wrong beliefs, key → first-person statement. Keys, never answers:
    /// naming how a learner is likely wrong is the opposite of handing them what is right (C-5).
    IReadOnlyDictionary<string, string> KnownMisconceptions,

    /// The shape the item must take, declared by the pack and carried here. The AI plane defaults
    /// to prose when it is absent, which is every caller written before M17.
    ItemFormat ItemFormat = ItemFormat.Prose);

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
    string Fingerprint,

    /// A key from the `GenerateItemRequest.KnownMisconceptions` the core sent — or null. Never free
    /// text: the AI plane's airlock (M14.2) guarantees only a key the core itself declared in
    /// advance ever comes back here.
    string? TargetsMisconception,

    /// The text under study, for a reading item. The learner reads it; it is not a secret. Null for
    /// a `Prose` item, which has no passage.
    ///
    /// Defaulted, not a new required position, so every prose-item call site written before M17
    /// keeps compiling unchanged.
    string? Passage = null,

    /// The choices as shown, in order. **Not a secret** — they are on the screen. Which one is
    /// correct is not here, and there is no field it could be put in.
    IReadOnlyList<string>? Options = null);
