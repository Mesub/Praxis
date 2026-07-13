using System.Security.Cryptography;
using System.Text;
using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **Flip one byte of one record → verification fails, and names the broken link.**
///
/// This is the capstone of the Ledger: the claim that there is **no byte anywhere in a signed record
/// a forger could change without being caught.** It is proved two ways — by fuzzing the actual bytes
/// the hash commits to (nothing is un-covered), and by tampering a field and checking `Verify` breaks
/// at the right link with a reason a human can read.
public sealed class ByteMutationTests
{
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Evidence Ev(int seq, double score, double assurance)
        => Evidence.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EvidenceType.Apply, GradingMethod.KeyMatch, score, assurance, seq, When).Value;

    private static List<LedgerEntry> Chain(int count)
    {
        var entries = new List<LedgerEntry>();
        var previous = LedgerChain.GenesisHash;

        for (var i = 0; i < count; i++)
        {
            var entry = LedgerChain.Append(Ev(i + 1, 0.6 + (i * 0.01), 0.9), previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    // ── Nothing committed is un-covered ──────────────────────────────────────

    /// **Every single byte of what the hash commits to matters.**
    ///
    /// For a real record, walk its hash preimage byte by byte, flip each one to something else, and
    /// confirm the hash changes *every time*. If even one byte could flip without moving the hash, that
    /// is a byte a forger could edit invisibly — and there is none.
    [Fact]
    public void No_single_byte_of_a_record_can_change_without_changing_its_hash()
    {
        var chain = Chain(3);
        var entry = chain[1];

        var preimage = Encoding.UTF8.GetBytes(LedgerChain.Preimage(entry.Evidence, entry.PreviousHash));
        var baseline = Digest(preimage);

        for (var i = 0; i < preimage.Length; i++)
        {
            var mutated = (byte[])preimage.Clone();
            mutated[i] ^= 0xFF;   // flip every bit of this byte — a maximal single-byte change

            Assert.NotEqual(baseline, Digest(mutated));
        }
    }

    // ── Tampering any field breaks Verify, at the right link ─────────────────

    /// **Edit any field of any record, and `Verify` breaks at exactly that record and says so.** One
    /// case per field, one record in the middle of the chain, so the naming is exercised.
    [Theory]
    [MemberData(nameof(FieldMutations))]
    public void Editing_any_field_breaks_the_chain_at_that_record(string _, Func<Evidence, Evidence> mutate)
    {
        var chain = Chain(6);

        var target = chain[3].Evidence;
        chain[3] = chain[3] with { Evidence = mutate(target) };

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(3, result.BrokenAtIndex);
        Assert.Contains("tampered with", result.Reason!, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> FieldMutations()
    {
        yield return ["Score", (Func<Evidence, Evidence>)(e => e with { Score = e.Score + 0.01 })];
        yield return ["Assurance", (Func<Evidence, Evidence>)(e => e with { Assurance = e.Assurance - 0.05 })];
        yield return ["SourceSequence", (Func<Evidence, Evidence>)(e => e with { SourceSequence = e.SourceSequence + 1 })];
        yield return ["ConceptId", (Func<Evidence, Evidence>)(e => e with { ConceptId = Guid.CreateVersion7() })];
        yield return ["LearnerId", (Func<Evidence, Evidence>)(e => e with { LearnerId = Guid.CreateVersion7() })];
        yield return ["Type", (Func<Evidence, Evidence>)(e => e with { Type = EvidenceType.Build })];
        yield return ["GradingMethod", (Func<Evidence, Evidence>)(e => e with { GradingMethod = GradingMethod.Rubric })];
        yield return ["ProducedAt", (Func<Evidence, Evidence>)(e => e with { ProducedAt = When.AddTicks(1) })];
        yield return ["Id", (Func<Evidence, Evidence>)(e => e with { Id = Guid.CreateVersion7() })];
    }

    /// **The break is named at the first tampered record, wherever it is.** Tamper record 0, 4, or the
    /// last, and the reported index is that record.
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    public void The_named_broken_link_is_the_tampered_record(int index)
    {
        var chain = Chain(8);

        var e = chain[index].Evidence;
        chain[index] = chain[index] with { Evidence = e with { Score = 0.123 } };

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(index, result.BrokenAtIndex);
    }

    // ── The signature and the checkpoint ─────────────────────────────────────

    /// **Flip one byte of the signature → it no longer verifies.**
    [Fact]
    public void Flipping_one_byte_of_the_signature_fails_verification()
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();
        var head = Chain(4)[^1].Hash;

        var signature = LedgerSigning.Sign(head, privateKey);
        Assert.True(LedgerSigning.Verify(head, signature, publicKey));

        for (var i = 0; i < signature.Length; i++)
        {
            var mutated = (byte[])signature.Clone();
            mutated[i] ^= 0x01;   // a single-bit flip

            Assert.False(
                LedgerSigning.Verify(head, mutated, publicKey),
                $"A one-bit change to signature byte {i} still verified.");
        }
    }

    /// **Flip one byte of the signed checkpoint payload → the signature no longer matches it.** The
    /// root, the count and the head are all inside what was signed, so none can be edited undetected.
    [Fact]
    public void Editing_one_field_of_the_signed_checkpoint_breaks_the_signature()
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();
        var chain = Chain(5);

        var checkpoint = new LedgerCheckpoint(chain[^1].Hash, LedgerMerkle.Root(chain), chain.Count, When, []);
        var signature = LedgerSigning.Sign(checkpoint.SignedPayload, privateKey);

        Assert.True(LedgerSigning.Verify(checkpoint.SignedPayload, signature, publicKey));

        foreach (var tampered in new[]
        {
            checkpoint with { RecordCount = checkpoint.RecordCount + 1 },
            checkpoint with { MerkleRoot = new string('e', 64) },
            checkpoint with { HeadHash = new string('f', 64) },
        })
        {
            Assert.False(LedgerSigning.Verify(tampered.SignedPayload, signature, publicKey));
        }
    }

    // ── End to end: one byte of a Verified Competency Record ─────────────────

    /// **Change one character of a competency's evidence hash and the whole record fails to verify,
    /// naming the competency.** This is the guarantee an employer relies on: you cannot doctor a record
    /// and have it still check out.
    [Fact]
    public void One_altered_character_in_a_competency_fails_the_whole_record()
    {
        var chain = Chain(6);
        var leaves = chain.Select(e => e.Hash).ToList();
        var root = LedgerMerkle.Root(chain);
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();

        var checkpoint = new LedgerCheckpoint(chain[^1].Hash, root, chain.Count, When, []);
        var signature = LedgerSigning.Sign(checkpoint.SignedPayload, privateKey);

        var genuine = new VerifiedCompetency(
            chain[2].Evidence.ConceptId, "Concurrency", chain[2].Evidence.Type,
            chain[2].Evidence.GradingMethod, chain[2].Evidence.Score, chain[2].Evidence.Assurance,
            chain[2].Hash, LedgerMerkle.Proof(leaves, 2));

        var record = new CompetencyRecord(
            "A. Learner", [genuine], checkpoint, signature, publicKey, When);

        Assert.True(CompetencyRecords.Verify(record, publicKey).IsValid);

        // Flip one hex character of the evidence hash — the leaf no longer proves out against the root.
        var chars = genuine.EvidenceHash.ToCharArray();
        chars[10] = chars[10] == 'a' ? 'b' : 'a';
        var doctored = record with { Competencies = [genuine with { EvidenceHash = new string(chars) }] };

        var result = CompetencyRecords.Verify(doctored, publicKey);

        Assert.False(result.IsValid);
        Assert.Contains("Concurrency", result.Reason!, StringComparison.Ordinal);
        Assert.Contains("does not prove out", result.Reason!, StringComparison.Ordinal);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
