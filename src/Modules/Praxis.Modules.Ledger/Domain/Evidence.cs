using Praxis.Shared;

namespace Praxis.Modules.Ledger.Domain;

/// **One piece of proof, as the Ledger records it forever.**
///
/// The Ledger is the moat and the money (I-8): an append-only, tamper-evident, signed record of every
/// piece of evidence a learner ever produced, exportable as a Verified Competency Record that a third
/// party can check item by item. All of that rests on this record being two things — **immutable** and
/// **honest** — and this file enforces both structurally rather than by good intentions.
///
/// ## Immutable
///
/// There are no setters. Once an `Evidence` is created it is frozen, because the whole value of an
/// append-only ledger evaporates the moment a single row can be edited after the fact. The hash chain
/// (M10.2) commits to these bytes; if the bytes could change, the chain would be committing to a
/// promise nobody keeps.
///
/// ## Honest about how much it can be trusted (C-6, C-4)
///
/// This is the subtle one, and it is the reason PRAXIS does not need to *catch* cheating — it only
/// needs to *record the assurance honestly* and let the truth stand.
///
/// **Assurance** is *how much we trust this proof*, and it is **not the same as the score.** A learner
/// can score 1.0 on an EXPLAIN — a perfect, eloquent answer — and PRAXIS still cannot be *certain* they
/// understand it, because **a language model graded it**, and a language model's opinion is exactly what
/// the Evidence Ceiling refuses to trust. So a rubric-graded piece of evidence has an assurance
/// **ceiling below 1.0**, and this record **refuses to be constructed claiming more.** You cannot write
/// down "we are 100% sure of this" about something only a model checked — not because a policy forbids
/// it, but because `Record` returns a failure.
///
/// A BUILD that *ran* against hidden tests, or a PREDICT the *world* resolved, has no such ceiling:
/// reality graded it, and reality does not have opinions. Those can be recorded at full assurance,
/// which is exactly why they are the evidence types that can confer mastery.
public sealed record Evidence
{
    private Evidence() { }

    /// A stable identity, assigned at creation. The chain and the signature commit to it.
    public Guid Id { get; private init; }

    public Guid LearnerId { get; private init; }

    public Guid ConceptId { get; private init; }

    /// What kind of proof this is (RECALL … PREDICT). Domain language, shared across engines.
    public EvidenceType Type { get; private init; }

    /// **How it was graded**, which is what determines how far it can be trusted.
    public GradingMethod GradingMethod { get; private init; }

    /// The graded result, in [0, 1]. *What* they achieved.
    public double Score { get; private init; }

    /// **How much PRAXIS trusts that result**, in [0, 1]. *How sure we are* — a different axis from the
    /// score, and the one the Evidence Ceiling caps. Recorded honestly or not at all.
    public double Assurance { get; private init; }

    /// The position in the event log this evidence was derived from. **Provenance**: the Ledger never
    /// records a conclusion it cannot trace back to the fact that produced it, which is what makes the
    /// whole thing rebuildable from zero (the kernel's one rule).
    public long SourceSequence { get; private init; }

    public DateTime ProducedAt { get; private init; }

    /// **Write a piece of evidence into the record — or refuse.**
    ///
    /// Refuses when the score or assurance is off-scale, and — the load-bearing one — **when the
    /// assurance claims more trust than the grading method can honestly support.** A rubric grade
    /// recorded at 0.95 assurance is a lie the Ledger will not tell.
    public static Result<Evidence> Record(
        Guid learnerId,
        Guid conceptId,
        EvidenceType type,
        GradingMethod gradingMethod,
        double score,
        double assurance,
        long sourceSequence,
        DateTime producedAt)
    {
        if (learnerId == Guid.Empty || conceptId == Guid.Empty)
        {
            throw new ArgumentException("Evidence must belong to a learner and a concept.");
        }

        if (score is < 0.0 or > 1.0)
        {
            return Result<Evidence>.Invalid("A score is a number between 0 and 1.");
        }

        if (assurance is < 0.0 or > 1.0)
        {
            return Result<Evidence>.Invalid("Assurance is a number between 0 and 1.");
        }

        var ceiling = gradingMethod.AssuranceCeiling();

        if (assurance > ceiling + 1e-9)
        {
            // **The honesty refusal.** We are being asked to record more certainty than the way this
            // was graded can support. A model graded it; a model can be wrong; and the Ledger will not
            // put a number on this that says otherwise.
            return Result<Evidence>.Invalid(
                $"This evidence was graded by {gradingMethod} and cannot be recorded at assurance "
                + $"{assurance:F2} — the honest ceiling for a {gradingMethod} grade is "
                + $"{ceiling:F2}, because that is as sure as PRAXIS can be about a result it did not "
                + "get from reality itself. Record it honestly or not at all.");
        }

        if (producedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("ProducedAt must be UTC.", nameof(producedAt));
        }

        return Result<Evidence>.Success(new Evidence
        {
            Id = Guid.CreateVersion7(),
            LearnerId = learnerId,
            ConceptId = conceptId,
            Type = type,
            GradingMethod = gradingMethod,
            Score = score,
            Assurance = assurance,
            SourceSequence = sourceSequence,
            ProducedAt = producedAt,
        });
    }
}

/// **How a piece of evidence was graded — which is what decides how far it can be trusted.**
///
/// This is a distinct axis from `EvidenceType.IsCeilingBound` (which is about *mastery weight* — can
/// this type cross the hold threshold). Grading method is about *assurance* — how certain we are the
/// grade is right. RECALL is ceiling-bound for mastery yet graded with perfect certainty (a string
/// matched a string); an EXPLAIN can confer real mastery yet is graded by a model and so is never
/// certain. Two different questions, kept apart on purpose (the same separation M7.1 drew).
public enum GradingMethod
{
    /// **It ran.** A BUILD against hidden tests — arithmetic over exit codes, no opinion in it (M9.2).
    Execution = 1,

    /// **The world resolved it.** A PREDICT the future settled — reality does not have opinions (M9.3).
    Reality = 2,

    /// **A string matched a string.** A RECALL or a keyed APPLY, checked deterministically. Certain
    /// about *this* answer — though weak as mastery, because a search engine can match a key too.
    KeyMatch = 3,

    /// **A language model graded it.** An EXPLAIN or a CRITIQUE against a rubric. Useful, sometimes the
    /// only way — and **never certain**, which is why its assurance is capped.
    Rubric = 4,
}

public static class GradingMethods
{
    /// **The most a model-graded result can honestly claim.** Below 1.0, and deliberately: a rubric
    /// grade can be excellent and still be wrong, and the gap between this and 1.0 is PRAXIS admitting
    /// it. Reality- and execution-graded evidence has no such gap, because it was not an opinion.
    public const double RubricAssuranceCeiling = 0.85;

    /// The honest ceiling on assurance for each way of grading.
    public static double AssuranceCeiling(this GradingMethod method) => method switch
    {
        GradingMethod.Execution => 1.0,
        GradingMethod.Reality => 1.0,
        GradingMethod.KeyMatch => 1.0,
        GradingMethod.Rubric => RubricAssuranceCeiling,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}
