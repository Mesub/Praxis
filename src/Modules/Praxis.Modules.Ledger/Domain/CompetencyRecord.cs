using Praxis.Shared;

namespace Praxis.Modules.Ledger.Domain;

/// **A résumé a stranger can verify — the cold-start weapon** (R-7, I-8).
///
/// A Verified Competency Record is what the whole Ledger is *for*: a document a learner hands to an
/// employer, a university, a regulator, who can then check — **without an account, without trusting
/// the learner, and without asking PRAXIS** — that every claim in it is real. That last part is the
/// network effect: the more places a PRAXIS record is accepted, the more a PRAXIS record is worth.
///
/// ## What makes it verifiable
///
/// Each competency carries the **evidence that backs it** and a **Merkle inclusion proof** that the
/// evidence sits under a root PRAXIS **signed**. A verifier needs three things, all public: the record,
/// the PRAXIS public key, and a hash function. They check the signature (this really is a PRAXIS
/// record), then each inclusion proof (this competency really is in it). Change a score, add a
/// competency nobody earned, inflate an assurance — any of it — and a proof stops reaching the root or
/// the signature stops verifying. **The forgery is not caught by us; it is caught by anyone.**
///
/// ## What it will not do
///
/// It will not claim more assurance than the evidence supports (the assurance travels straight from
/// the `Evidence`, honestly — a competency held only on a model's say-so *says so*). And it exports
/// nothing a verifier cannot check: there is no "trust me" field, because a claim a third party cannot
/// verify is exactly the thing this record exists to replace.
public sealed record CompetencyRecord(
    /// Who it is about, as they choose to be known. Not proof of identity — that is the verifier's job
    /// (they know who handed them the record); this binds competencies to a subject label.
    string Subject,

    IReadOnlyList<VerifiedCompetency> Competencies,

    /// The signed commitment the whole record hangs from: the Merkle root, the record count, and
    /// PRAXIS's signature over them.
    LedgerCheckpoint Checkpoint,

    /// The signature over the checkpoint's payload — carried alongside so a verifier has everything in
    /// one document.
    byte[] Signature,

    /// PRAXIS's public key, **also published independently.** A verifier should compare this against
    /// the key they already trust — a record that carries its own key proves only internal
    /// consistency, exactly as the hash chain does. The trust root is the *published* key, not this
    /// copy.
    byte[] PublicKey,

    DateTime IssuedAt);

/// One competency, and the cryptographic backing that makes it checkable.
public sealed record VerifiedCompetency(
    Guid ConceptId,
    string ConceptName,
    EvidenceType Type,
    GradingMethod GradingMethod,

    double Score,

    /// **Honest, straight from the evidence.** A competency backed only by a model-graded rubric shows
    /// its capped assurance; the record does not launder it into certainty (C-6).
    double Assurance,

    /// The hash of the ledger entry that proves this, and the inclusion proof that it sits under the
    /// signed root.
    string EvidenceHash,
    MerkleProof Proof);

/// Building and verifying a `CompetencyRecord`.
public static class CompetencyRecords
{
    /// **Verify a record end to end — the exact logic a public verifier runs, and it needs no
    /// secret.**
    ///
    /// `trustedPublicKey` is the key the verifier already trusts (from PRAXIS's published key, a
    /// well-known registry — *not* the copy inside the record). Everything checked here can be checked
    /// by anyone, offline, forever.
    public static RecordVerification Verify(CompetencyRecord record, byte[] trustedPublicKey)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(trustedPublicKey);

        // 1. Is this really a PRAXIS record? The signature must verify against the key the verifier
        //    trusts — never merely against the key the record carries, which a forger controls.
        if (!LedgerSigning.Verify(record.Checkpoint.SignedPayload, record.Signature, trustedPublicKey))
        {
            return RecordVerification.Fail(
                "The signature does not verify against the trusted PRAXIS key. This record was not "
                + "issued by PRAXIS, or it has been altered since it was.");
        }

        // 2. Does the checkpoint's own count agree with what the record shows? A record cannot claim
        //    more competencies than the ledger it points at contains — but competencies can be fewer
        //    (a selective disclosure of some of your record is legitimate), so this is a *ceiling*.
        if (record.Competencies.Count > record.Checkpoint.RecordCount)
        {
            return RecordVerification.Fail(
                $"The record lists {record.Competencies.Count} competencies but the signed ledger "
                + $"holds only {record.Checkpoint.RecordCount}. Something has been added that PRAXIS "
                + "never signed.");
        }

        // 3. Is each competency actually in the signed root? The inclusion proof is the whole point —
        //    a competency whose proof does not reach the root was invented after signing.
        for (var i = 0; i < record.Competencies.Count; i++)
        {
            var c = record.Competencies[i];

            if (!LedgerMerkle.VerifyProof(c.EvidenceHash, c.Proof, record.Checkpoint.MerkleRoot))
            {
                return RecordVerification.Fail(
                    $"'{c.ConceptName}' does not prove out against the signed root. This competency "
                    + "was not in the ledger PRAXIS signed — it has been added or altered.");
            }
        }

        return RecordVerification.Ok(record.Competencies.Count);
    }
}

/// The result a verifier gets: verified, or a plain-language reason it is not.
public sealed record RecordVerification(bool IsValid, int VerifiedCompetencies, string? Reason)
{
    public static RecordVerification Ok(int count) => new(true, count, null);

    public static RecordVerification Fail(string reason) => new(false, 0, reason);
}
