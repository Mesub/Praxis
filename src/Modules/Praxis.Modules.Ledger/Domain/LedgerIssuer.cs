using Praxis.Shared;

namespace Praxis.Modules.Ledger.Domain;

/// **Issue a Verified Competency Record — the export a learner hands to an employer.**
///
/// This is the *other half* of `CompetencyRecords.Verify`: verification needs no secret and anyone
/// can run it; issuance needs PRAXIS's private key and only PRAXIS can. So the signing is not done
/// here — it is handed in as a delegate, because the domain must never hold the key. The composition
/// root owns the key and provides a `sign` operation; this builds everything the signature commits
/// to and asks for one signature over it.
///
/// What it commits to, and what it will not launder: each competency carries its **honest**
/// assurance straight from the evidence (a model-graded rubric shows its capped assurance, C-6), the
/// Merkle proof that it sits under the signed root, and the grading method that produced it. A record
/// with nothing proved yet is a real, valid record of zero competencies — an empty ledger is not an
/// error, and issuing one that says "nothing here" is more honest than refusing to issue.
public static class LedgerIssuer
{
    /// One thing the learner has proved, as the Twin knows it — ready to be committed to the chain.
    public sealed record Held(
        Guid ConceptId,
        string ConceptName,
        EvidenceType Type,
        GradingMethod Method,
        double Score,
        double Assurance,
        int SourceSequence);

    /// Build and sign a record. `sign` covers the checkpoint's payload (head + root + count + time)
    /// with PRAXIS's private key; the key never comes near this method.
    public static CompetencyRecord Issue(
        Guid learnerId,
        string subject,
        IReadOnlyList<Held> held,
        Func<string, byte[]> sign,
        byte[] publicKey,
        DateTime issuedAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(sign);
        ArgumentNullException.ThrowIfNull(publicKey);

        // Build the hash chain — one entry per proved competency, each linking to the last.
        var entries = new List<LedgerEntry>(held.Count);
        var previous = LedgerChain.GenesisHash;

        foreach (var h in held)
        {
            var evidence = Evidence.Record(
                learnerId, h.ConceptId, h.Type, h.Method, h.Score, h.Assurance, h.SourceSequence, issuedAt);

            if (!evidence.IsSuccess)
            {
                // The caller handed dishonest evidence — e.g. a rubric-graded competency claiming
                // more assurance than a model's opinion can carry (C-6). That is a bug in whatever
                // built the snapshot, not a learner outcome, and it must not be signed.
                throw new InvalidOperationException(
                    $"Cannot issue '{h.ConceptName}': {evidence.Message}");
            }

            var entry = LedgerChain.Append(evidence.Value, previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        var leaves = entries.Select(e => e.Hash).ToList();
        var head = entries.Count == 0 ? LedgerChain.GenesisHash : entries[^1].Hash;

        var checkpoint = new LedgerCheckpoint(
            head, LedgerMerkle.RootOf(leaves), entries.Count, issuedAt, []);

        var signature = sign(checkpoint.SignedPayload);

        var competencies = new List<VerifiedCompetency>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            competencies.Add(new VerifiedCompetency(
                held[i].ConceptId,
                held[i].ConceptName,
                held[i].Type,
                held[i].Method,
                held[i].Score,
                held[i].Assurance,
                entries[i].Hash,
                LedgerMerkle.Proof(leaves, i)));
        }

        return new CompetencyRecord(subject, competencies, checkpoint, signature, publicKey, issuedAt);
    }
}
