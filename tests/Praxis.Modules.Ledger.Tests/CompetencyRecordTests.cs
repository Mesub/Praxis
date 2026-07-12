using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **A record a stranger can verify — and cannot forge.**
///
/// These build a real signed ledger, export a Verified Competency Record from it, and check that the
/// verification a public endpoint would run accepts the genuine article and rejects every tampering.
public sealed class CompetencyRecordTests
{
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Evidence Ev(int seq)
        => Evidence.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EvidenceType.Build, GradingMethod.Execution, 1.0, 1.0, seq, When).Value;

    private static List<LedgerEntry> Chain(int count)
    {
        var entries = new List<LedgerEntry>();
        var previous = LedgerChain.GenesisHash;

        for (var i = 0; i < count; i++)
        {
            var entry = LedgerChain.Append(Ev(i + 1), previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    // ── Merkle inclusion proofs ──────────────────────────────────────────────

    /// **Every leaf proves out against its own root.** Whatever the tree's shape — odd counts,
    /// duplicated last nodes — an inclusion proof for any index folds back up to the root.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    public void Every_leaf_has_a_proof_that_reaches_the_root(int count)
    {
        var leaves = Chain(count).Select(e => e.Hash).ToList();
        var root = LedgerMerkle.RootOf(leaves);

        for (var i = 0; i < count; i++)
        {
            var proof = LedgerMerkle.Proof(leaves, i);

            Assert.True(
                LedgerMerkle.VerifyProof(leaves[i], proof, root),
                $"Leaf {i} of {count} did not prove out against its own root.");
        }
    }

    /// **A proof for one leaf does not verify a different leaf.** You cannot take your neighbour's
    /// proof and staple it to your own (missing) evidence.
    [Fact]
    public void A_proof_does_not_verify_a_leaf_it_was_not_made_for()
    {
        var leaves = Chain(8).Select(e => e.Hash).ToList();
        var root = LedgerMerkle.RootOf(leaves);

        var proofForThree = LedgerMerkle.Proof(leaves, 3);

        Assert.False(LedgerMerkle.VerifyProof(leaves[4], proofForThree, root));
    }

    /// **A leaf that is not in the tree cannot be proven into it.** A fabricated competency has no
    /// path to the root.
    [Fact]
    public void A_fabricated_leaf_cannot_be_proven_into_the_tree()
    {
        var leaves = Chain(8).Select(e => e.Hash).ToList();
        var root = LedgerMerkle.RootOf(leaves);

        var invented = new string('c', 64);
        var stolenProof = LedgerMerkle.Proof(leaves, 2);

        Assert.False(LedgerMerkle.VerifyProof(invented, stolenProof, root));
    }

    [Fact]
    public void A_malformed_proof_verifies_as_false_not_a_crash()
    {
        var leaves = Chain(4).Select(e => e.Hash).ToList();
        var root = LedgerMerkle.RootOf(leaves);

        var garbage = new MerkleProof([new ProofStep("not-hex", true)]);

        Assert.False(LedgerMerkle.VerifyProof(leaves[0], garbage, root));
        Assert.False(LedgerMerkle.VerifyProof("not-hex", LedgerMerkle.Proof(leaves, 0), root));
    }

    // ── The whole record: verifiable by a stranger, forgeable by no one ──────

    private static (CompetencyRecord Record, byte[] TrustedKey) IssueRecord(int chainSize = 6)
    {
        var chain = Chain(chainSize);
        var leaves = chain.Select(e => e.Hash).ToList();
        var root = LedgerMerkle.Root(chain);
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();

        var checkpoint = new LedgerCheckpoint(chain[^1].Hash, root, chain.Count, When, [])
        {
            // Sign the payload; the Signature field on the checkpoint itself is not part of what is
            // signed (that would be circular).
        };

        var signature = LedgerSigning.Sign(checkpoint.SignedPayload, privateKey);

        // Expose two of the learner's competencies with their inclusion proofs. Indices small enough
        // that a two-record ledger still has them.
        var competencies = new[]
        {
            Competency(chain, leaves, index: 0, "Rate limiting"),
            Competency(chain, leaves, index: 1, "Binary search"),
        };

        var record = new CompetencyRecord(
            Subject: "A. Learner",
            Competencies: competencies,
            Checkpoint: checkpoint,
            Signature: signature,
            PublicKey: publicKey,
            IssuedAt: When);

        return (record, publicKey);
    }

    private static VerifiedCompetency Competency(
        List<LedgerEntry> chain, List<string> leaves, int index, string name)
    {
        var e = chain[index].Evidence;

        return new VerifiedCompetency(
            e.ConceptId, name, e.Type, e.GradingMethod, e.Score, e.Assurance,
            EvidenceHash: chain[index].Hash,
            Proof: LedgerMerkle.Proof(leaves, index));
    }

    /// **The genuine article verifies — against a key the verifier already trusts, needing no
    /// account and no call home.**
    [Fact]
    public void A_genuine_record_verifies_against_the_trusted_key()
    {
        var (record, trustedKey) = IssueRecord();

        var result = CompetencyRecords.Verify(record, trustedKey);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(2, result.VerifiedCompetencies);
    }

    /// **A record signed by anyone but PRAXIS is rejected.** The trust root is the *published* key,
    /// not the key the record carries — a forger can put their own key in the document, but the
    /// verifier checks against the one they already trust.
    [Fact]
    public void A_record_signed_by_the_wrong_key_is_rejected()
    {
        var (record, _) = IssueRecord();

        var (_, someoneElsesKey) = LedgerSigning.GenerateKeyPair();

        var result = CompetencyRecords.Verify(record, someoneElsesKey);

        Assert.False(result.IsValid);
        Assert.Contains("not issued by PRAXIS", result.Reason!, StringComparison.Ordinal);
    }

    /// **You cannot inflate a score in the exported record.** The evidence hash covers the score, so
    /// editing the displayed score leaves the hash pointing at a leaf that no longer matches — the
    /// proof still reaches the root, but the hash the learner would have to present for the *inflated*
    /// evidence is not in the tree.
    [Fact]
    public void Adding_a_competency_that_is_not_in_the_ledger_is_rejected()
    {
        var (record, trustedKey) = IssueRecord();

        // Bolt on a competency backed by a hash that is not in the signed tree, with a stolen proof.
        var forged = record.Competencies[0] with
        {
            ConceptName = "Distributed consensus (never earned)",
            EvidenceHash = new string('d', 64),
        };

        var tampered = record with { Competencies = [.. record.Competencies, forged] };

        var result = CompetencyRecords.Verify(tampered, trustedKey);

        Assert.False(result.IsValid);
        Assert.Contains("does not prove out", result.Reason!, StringComparison.Ordinal);
    }

    /// **You cannot claim more competencies than the signed ledger holds.** The count is signed, and
    /// a record listing more than the ledger contains is rejected before any proof is even checked.
    [Fact]
    public void Claiming_more_competencies_than_the_ledger_holds_is_rejected()
    {
        var (record, trustedKey) = IssueRecord(chainSize: 2);

        // Two real competencies from a two-record ledger is fine; a third, however proven, cannot be —
        // the signed count is 2.
        var padded = record with
        {
            Competencies = [.. record.Competencies, record.Competencies[0], record.Competencies[1]],
        };

        var result = CompetencyRecords.Verify(padded, trustedKey);

        Assert.False(result.IsValid);
        Assert.Contains("never signed", result.Reason!, StringComparison.Ordinal);
    }

    /// **Selective disclosure is legitimate** — showing *some* of your record, not all, still
    /// verifies. Privacy and verifiability are not in tension here.
    [Fact]
    public void Showing_only_some_competencies_still_verifies()
    {
        var (record, trustedKey) = IssueRecord(chainSize: 6);

        var oneOnly = record with { Competencies = [record.Competencies[0]] };

        Assert.True(CompetencyRecords.Verify(oneOnly, trustedKey).IsValid);
    }

    /// **Assurance is exported honestly.** A model-graded competency carries its capped assurance into
    /// the record, not a laundered 1.0.
    [Fact]
    public void A_model_graded_competency_exports_its_honest_capped_assurance()
    {
        var explain = Evidence.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EvidenceType.Explain, GradingMethod.Rubric, 1.0,
            GradingMethods.RubricAssuranceCeiling, 1, When).Value;

        var entry = LedgerChain.AppendFirst(explain);

        var competency = new VerifiedCompetency(
            explain.ConceptId, "An explained concept", explain.Type, explain.GradingMethod,
            explain.Score, explain.Assurance, entry.Hash, LedgerMerkle.Proof([entry.Hash], 0));

        Assert.Equal(GradingMethods.RubricAssuranceCeiling, competency.Assurance);
        Assert.True(competency.Assurance < 1.0);
    }
}
