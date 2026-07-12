using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **Authenticity and un-rewritability — the two things the hash chain cannot give on its own.**
public sealed class LedgerSigningTests
{
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Evidence AnyEvidence(int seq = 1)
        => Evidence.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EvidenceType.Build, GradingMethod.Execution, 1.0, 1.0, seq, When).Value;

    private static List<LedgerEntry> Chain(int count)
    {
        var entries = new List<LedgerEntry>();
        var previous = LedgerChain.GenesisHash;

        for (var i = 0; i < count; i++)
        {
            var entry = LedgerChain.Append(AnyEvidence(i + 1), previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    // ── Signing: the head is unforgeable ─────────────────────────────────────

    /// **A signature made with the private key verifies with the public key** — the round trip that
    /// everything else rests on.
    [Fact]
    public void A_signed_head_verifies_with_the_public_key()
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();

        var head = Chain(5)[^1].Hash;

        var signature = LedgerSigning.Sign(head, privateKey);

        Assert.True(LedgerSigning.Verify(head, signature, publicKey));
    }

    /// **The load-bearing refusal: a forged chain cannot carry a valid PRAXIS signature.** An attacker
    /// who rewrites a record and recomputes every hash gets a chain that *internally* verifies — and it
    /// is worthless, because the signature over its new head does not check against the PRAXIS public
    /// key. They can rewrite the records; they cannot sign the lie.
    [Fact]
    public void A_forged_chain_cannot_be_signed_by_anyone_but_the_key_holder()
    {
        var (praxisPrivate, praxisPublic) = LedgerSigning.GenerateKeyPair();

        // PRAXIS signs the real head.
        var realHead = Chain(5)[^1].Hash;
        var realSignature = LedgerSigning.Sign(realHead, praxisPrivate);
        Assert.True(LedgerSigning.Verify(realHead, realSignature, praxisPublic));

        // The attacker builds a different, internally-consistent chain with a different head, and signs
        // it with the only key they have — their own.
        var (attackerPrivate, _) = LedgerSigning.GenerateKeyPair();
        var forgedHead = Chain(5)[^1].Hash;
        var forgedSignature = LedgerSigning.Sign(forgedHead, attackerPrivate);

        // Against the PRAXIS public key, the forgery does not verify.
        Assert.False(
            LedgerSigning.Verify(forgedHead, forgedSignature, praxisPublic),
            "A chain signed with the attacker's key verified against PRAXIS's key — signatures are "
            + "meaningless and every Verified Competency Record is forgeable.");
    }

    /// **A signature over one head does not vouch for another.** Reusing a valid signature on a
    /// different (rewritten) head fails — which is what stops a real signature being lifted onto a lie.
    [Fact]
    public void A_signature_does_not_transfer_to_a_different_head()
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();

        var signature = LedgerSigning.Sign(Chain(5)[^1].Hash, privateKey);

        var otherHead = Chain(6)[^1].Hash;

        Assert.False(LedgerSigning.Verify(otherHead, signature, publicKey));
    }

    /// **Bad input verifies as false, never as an exception.** A verifier that throws its way out of a
    /// check is a verifier an attacker controls.
    [Fact]
    public void Malformed_signatures_and_keys_verify_as_false_not_as_a_crash()
    {
        var (_, publicKey) = LedgerSigning.GenerateKeyPair();
        var head = Chain(1)[^1].Hash;

        Assert.False(LedgerSigning.Verify(head, [1, 2, 3], publicKey));            // too-short signature
        Assert.False(LedgerSigning.Verify(head, new byte[64], publicKey));         // wrong signature
        Assert.False(LedgerSigning.Verify(head, new byte[64], [9, 9, 9]));         // malformed key
    }

    // ── Merkle: history cannot be rewritten in secret ────────────────────────

    /// **The root pins every record.** Change one leaf and the published root changes — so a
    /// previously-published root is a commitment to exactly the history that produced it.
    [Fact]
    public void Altering_any_record_changes_the_published_root()
    {
        var chain = Chain(9);
        var rootBefore = LedgerMerkle.Root(chain);

        // Rewrite one leaf's stored hash — as if a record were edited and re-sealed.
        chain[4] = chain[4] with { Hash = new string('b', 64) };

        var rootAfter = LedgerMerkle.Root(chain);

        Assert.NotEqual(rootBefore, rootAfter);
    }

    /// The root is deterministic — the same records always fold to the same root, on any machine.
    [Fact]
    public void The_merkle_root_is_deterministic()
    {
        var chain = Chain(7);

        Assert.Equal(LedgerMerkle.Root(chain), LedgerMerkle.Root(chain));
    }

    /// **The classic Merkle second-preimage attack, constructed and defeated.**
    ///
    /// Without domain separation, an attacker can present a *different* tree with the *same* root: take
    /// the two-leaf tree over `[x, y]`, whose root is `H(H(x) ‖ H(y))`, and forge a **one-leaf** tree
    /// whose single leaf is the raw bytes `H(x) ‖ H(y)` — its root is `H(H(x) ‖ H(y))`, identical. Two
    /// different histories, one root, and a proof for one that verifies as a proof for the other.
    ///
    /// Domain separation closes it: a leaf is `H(0x00 ‖ …)` and an interior node is `H(0x01 ‖ …)`, so
    /// the forged leaf hashes to `H(0x00 ‖ H(x) ‖ H(y))`, which is not the interior node's
    /// `H(0x01 ‖ H(x) ‖ H(y))`. This builds the exact collision and asserts it does **not** happen.
    [Fact]
    public void The_second_preimage_attack_does_not_produce_a_colliding_root()
    {
        var twoLeafRoot = LedgerMerkle.RootOf(["aa", "bb"]);

        var l0 = LedgerMerkle.LeafHash("aa");
        var l1 = LedgerMerkle.LeafHash("bb");

        // Construction A — the raw concatenation. Collides if *neither* prefix is present.
        var forgeBoth = Convert.ToHexStringLower([.. l0, .. l1]);
        Assert.NotEqual(twoLeafRoot, LedgerMerkle.RootOf([forgeBoth]));

        // Construction B — the interior node's actual preimage, `0x01 ‖ l0 ‖ l1`, presented as a
        // single leaf. Collides if the *leaf* prefix is missing (the leaf would then hash its raw
        // bytes, matching the interior node exactly). The leaf `0x00` prefix is what defeats it.
        var forgeWithInteriorPrefix = Convert.ToHexStringLower([0x01, .. l0, .. l1]);
        Assert.NotEqual(twoLeafRoot, LedgerMerkle.RootOf([forgeWithInteriorPrefix]));
    }

    [Fact]
    public void An_empty_ledger_has_a_well_known_root()
    {
        Assert.Equal(LedgerMerkle.EmptyRoot, LedgerMerkle.Root([]));
    }

    // ── The checkpoint binds head, root and count together ───────────────────

    /// **A published checkpoint signs head, root and count as one payload**, so none can be swapped
    /// independently — and the count closes the duplicated-leaf hole a bare root would leave open.
    [Fact]
    public void A_checkpoint_signs_head_root_and_count_as_one_thing()
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();
        var chain = Chain(5);

        var checkpoint = new LedgerCheckpoint(
            HeadHash: chain[^1].Hash,
            MerkleRoot: LedgerMerkle.Root(chain),
            RecordCount: chain.Count,
            PublishedAt: When,
            Signature: []);

        var signature = LedgerSigning.Sign(checkpoint.SignedPayload, privateKey);

        Assert.True(LedgerSigning.Verify(checkpoint.SignedPayload, signature, publicKey));

        // Swap the count — as if claiming the same root covers a different number of records — and the
        // signature no longer matches the payload.
        var tampered = checkpoint with { RecordCount = 6 };
        Assert.False(LedgerSigning.Verify(tampered.SignedPayload, signature, publicKey));
    }
}
