using Praxis.Shared;

namespace Praxis.Modules.Twin.Learning;

/// **The Evidence Ceiling (CANON C-4), enforced in the belief update.**
///
/// This is the most important function in PRAXIS, and the one that has to be right when everything
/// else is wrong.
///
/// The claim the product makes to the world is that a Ledger entry *means something* — that when it
/// says a person demonstrated a concept, they **did something a machine could not do for them**.
///
/// That claim is worth exactly as much as this file. So the guarantee is enforced twice, in two
/// engines that do not know about each other:
///
///   **The Graph** refuses to *author* a contract where model-graded evidence could reach the hold
///   threshold. (M2.2 — arithmetic, at construction.)
///
///   **The Twin** — here — refuses to let model-graded evidence *carry* a learner over the
///   threshold, even if such a contract somehow existed. It recomputes the ceiling from the
///   contract it was handed and clamps against it, rather than trusting that someone upstream did
///   their job.
///
/// Belt and braces, on purpose — and the two are **not** redundant. They catch different failures,
/// which was confirmed by deleting the clamp below and watching what broke:
///
///   With an **honest** contract, the per-clause caps alone already hold the line. Nothing failed.
///   That is the Graph's guarantee doing its job.
///
///   With a **corrupt** contract — one the Graph would never author, but which a bad migration or a
///   hand-written row could produce — the clamp is the *only* thing standing between a learner and
///   a mastery certificate made of explanations. That test failed immediately.
///
/// A single point of failure guarding the only thing that makes the product honest is not a design,
/// it is a hope.
///
/// ## What it does, in one sentence
///
/// Each evidence type contributes at most what its clause allows, no matter how many times the
/// learner produces it or how highly it scores — and the model-graded types, together, cannot
/// reach the threshold. **Ten perfect explanations are not a build that runs.**
public static class MasteryCredit
{
    /// Compute mastery credit from everything the learner has produced.
    ///
    /// Pure: a function of the evidence and the contract, nothing else. Replay the same evidence and
    /// you get the same credit, which is what makes the Twin rebuildable and the Ledger verifiable.
    public static MasteryAssessment Compute(
        MasteryContract contract,
        IReadOnlyList<AcceptedEvidenceRecord> evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);

        var creditByType = new Dictionary<EvidenceType, double>();

        foreach (var clause in contract.Clauses)
        {
            var qualifying = evidence
                .Where(e => e.Type == clause.Type && e.Score >= clause.MinScore)
                .OrderByDescending(e => e.Score * e.Assurance)
                .ToList();

            if (qualifying.Count == 0)
            {
                continue;
            }

            // How much of the required count did they actually produce? Producing one of two
            // required BUILDs is worth half the clause, not all of it.
            var progress = Math.Min(1.0, qualifying.Count / (double)clause.RequiredCount);

            // Quality is the mean of the best `RequiredCount` pieces, weighted by how much we trust
            // each one. A brilliant explanation that a model graded is still an explanation a model
            // graded.
            var best = qualifying.Take(clause.RequiredCount).ToList();
            var quality = best.Average(e => e.Score * e.Assurance);

            creditByType[clause.Type] = clause.MaxContribution * progress * quality;
        }

        var fromModelGraded = creditByType
            .Where(kv => kv.Key.IsModelGraded())
            .Sum(kv => kv.Value);

        var fromUnfakeable = creditByType
            .Where(kv => !kv.Key.IsModelGraded())
            .Sum(kv => kv.Value);

        // ── THE CEILING ──────────────────────────────────────────────────────
        //
        // Recomputed here from the contract, not trusted from upstream. Model-graded evidence is
        // clamped to strictly below the hold threshold, so that no quantity of it — at any score,
        // for any length of time — can carry a learner over the line on its own.
        //
        // The margin is subtracted so that a contract sitting exactly at the boundary still cannot
        // reach it. "Reaches the threshold" is not "stays below it".
        var permittedFromModel = Math.Min(
            contract.ModelGradedCeiling,
            Math.Max(0.0, contract.HoldThreshold - CeilingMargin));

        var cappedModelCredit = Math.Min(fromModelGraded, permittedFromModel);

        var total = Math.Clamp(cappedModelCredit + fromUnfakeable, 0.0, 1.0);

        // Held only if the total clears the bar — and, by construction, that is unreachable without
        // real work.
        var isHeld = total >= contract.HoldThreshold;

        // How much of what we believe rests on something a model could have fabricated. Recorded
        // honestly, and exported into the Ledger honestly (C-6).
        var assurance = total <= 0
            ? 0.0
            : Math.Clamp(fromUnfakeable / total, 0.0, 1.0);

        return new MasteryAssessment(
            MasteryCredit: total,
            IsHeld: isHeld,
            FromUnfakeableEvidence: fromUnfakeable,
            FromModelGradedEvidence: cappedModelCredit,
            Assurance: assurance);
    }

    /// The gap kept between what a model's opinion can be worth and the threshold it must not
    /// reach. Small, but strictly positive — equality is not safety.
    public const double CeilingMargin = 1e-9;
}

/// One accepted piece of evidence, as the Twin sees it.
///
/// `Assurance` is how much this particular proof can be trusted — a build that ran against hidden
/// tests is 1.0; an explanation a model liked, produced by a learner whose calibration says they
/// cannot predict themselves, is worth considerably less (M3.4).
public sealed record AcceptedEvidenceRecord(
    EvidenceType Type,
    double Score,
    double Assurance);

public sealed record MasteryAssessment(
    double MasteryCredit,
    bool IsHeld,
    double FromUnfakeableEvidence,
    double FromModelGradedEvidence,
    double Assurance);
