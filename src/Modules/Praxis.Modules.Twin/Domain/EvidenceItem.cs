using Praxis.Shared;

namespace Praxis.Modules.Twin.Domain;

/// One accepted piece of evidence, as the Twin remembers it.
///
/// **This is derived state, not a record of truth.** The Ledger (E16) is the tamper-evident record;
/// this is the Twin's working memory, and a rebuild throws it away and recomputes it from the log.
/// If they ever disagree, the log is right and this is wrong.
///
/// It exists because mastery credit is a function of *everything* a learner has produced — you
/// cannot compute "the best two APPLYs, capped at the clause maximum" from a running total. The
/// projection has to remember the pieces.
public sealed class EvidenceItem : BaseEntity
{
    public Guid LearnerId { get; private set; }
    public Guid ConceptId { get; private set; }

    /// The event that produced this. Makes the projection idempotent: replaying an event that has
    /// already been applied must not count it twice.
    public Guid EvidenceId { get; private set; }

    public EvidenceType Type { get; private set; }

    public double Score { get; private set; }

    /// How much this particular proof can be trusted. A build that ran against hidden tests is 1.0.
    /// An explanation a model liked, from a learner whose calibration says they cannot predict
    /// themselves, is worth considerably less (M3.4).
    public double Assurance { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private EvidenceItem() { }   // EF

    public static EvidenceItem Of(
        Guid learnerId,
        Guid conceptId,
        Guid evidenceId,
        EvidenceType type,
        double score,
        double assurance,
        DateTime occurredAt) => new()
        {
            LearnerId = learnerId,
            ConceptId = conceptId,
            EvidenceId = evidenceId,
            Type = type,
            Score = Math.Clamp(score, 0.0, 1.0),
            Assurance = Math.Clamp(assurance, 0.0, 1.0),
            OccurredAt = occurredAt,
        };
}
