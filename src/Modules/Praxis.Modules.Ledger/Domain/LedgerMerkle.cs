using System.Security.Cryptography;
using System.Text;

namespace Praxis.Modules.Ledger.Domain;

/// **Fold every record into one root — so that publishing the root pins all of them at once, and no
/// past record can be quietly rewritten.**
///
/// A signature (`LedgerSigning`) makes the *current* head unforgeable. But PRAXIS holds its own key,
/// so nothing stops PRAXIS re-signing a rewritten past. The fix is not to trust ourselves less; it is
/// to make rewriting *observable*. The records are hashed pairwise up a tree to a single 32-byte
/// **root**, and that root is published somewhere public and append-only. To alter one leaf you must
/// change the root — and the old root is already out in the world. **You cannot un-publish.**
///
/// That is the un-rewritability people reach for a blockchain to get, and here it is a hash function
/// and a noticeboard.
///
/// ## Determinism, again, for the same reason
///
/// A published root is only a commitment if the tree is rebuilt identically forever. So the
/// construction is fixed and spelled out: leaves are the entry hashes **in order**; a level with an
/// odd count **duplicates its last node** (a standard, and standardly-warned-about, convention);
/// interior nodes are `H(left ‖ right)`. `H` is domain-separated with a one-byte prefix — `0x00` for
/// leaves, `0x01` for interior nodes — so that no interior hash can ever be mistaken for a leaf, which
/// is the classic second-preimage hole in a naive Merkle tree.
public static class LedgerMerkle
{
    /// The root of the empty tree. A ledger with no records still has a well-known, checkable root.
    public const string EmptyRoot =
        "0000000000000000000000000000000000000000000000000000000000000000";

    /// Fold a chain's entry-hashes into a single Merkle root.
    public static string Root(IReadOnlyList<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return RootOf([.. entries.Select(e => e.Hash)]);
    }

    /// The root over a list of leaf hashes.
    public static string RootOf(IReadOnlyList<string> leafHashes)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (leafHashes.Count == 0)
        {
            return EmptyRoot;
        }

        // Level 0: the leaves, domain-separated so a leaf can never collide with an interior node.
        var level = leafHashes.Select(LeafHash).ToList();

        while (level.Count > 1)
        {
            var next = new List<byte[]>((level.Count + 1) / 2);

            for (var i = 0; i < level.Count; i += 2)
            {
                // Odd node out: duplicate it. Standard, and its one known weakness (a duplicated leaf
                // can be forged as a longer tree) is why the *count* is part of what gets signed at
                // the head — the root alone is never trusted in isolation.
                var right = i + 1 < level.Count ? level[i + 1] : level[i];

                next.Add(InteriorHash(level[i], right));
            }

            level = next;
        }

        return Convert.ToHexStringLower(level[0]);
    }

    /// **An inclusion proof: the handful of sibling hashes that prove one leaf is under the root**,
    /// without revealing any of the other leaves.
    ///
    /// This is what makes a Verifiable Competency Record *selective* — a learner can prove *"this one
    /// competency is in the ledger PRAXIS signed"* while showing nobody the rest of their record. The
    /// verifier walks the proof up from the leaf, and if it arrives at the published root, the leaf was
    /// there. Nothing about the other `count − 1` records leaks, and nothing can be forged into it.
    public static MerkleProof Proof(IReadOnlyList<string> leafHashes, int index)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, leafHashes.Count);

        var level = leafHashes.Select(LeafHash).ToList();
        var position = index;
        var steps = new List<ProofStep>();

        while (level.Count > 1)
        {
            // The sibling is the other half of this pair. An odd node out is paired with itself (the
            // same duplication convention the tree is built with, so proofs and roots agree).
            var siblingOnRight = position % 2 == 0;
            var siblingIndex = siblingOnRight ? position + 1 : position - 1;

            if (siblingIndex >= level.Count)
            {
                siblingIndex = position;   // the duplicated last node
            }

            steps.Add(new ProofStep(Convert.ToHexStringLower(level[siblingIndex]), siblingOnRight));

            var next = new List<byte[]>((level.Count + 1) / 2);

            for (var i = 0; i < level.Count; i += 2)
            {
                var right = i + 1 < level.Count ? level[i + 1] : level[i];
                next.Add(InteriorHash(level[i], right));
            }

            level = next;
            position /= 2;
        }

        return new MerkleProof(steps);
    }

    /// **Verify an inclusion proof against a root** — the whole of what a public verifier does for one
    /// record. Recompute the leaf hash, fold the siblings in, and check we reach the root.
    public static bool VerifyProof(string leafHash, MerkleProof proof, string expectedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(leafHash);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentException.ThrowIfNullOrEmpty(expectedRoot);

        byte[] running;

        try
        {
            running = LeafHash(leafHash);
        }
        catch (FormatException)
        {
            return false;   // not even a valid hex hash — never verifies, never throws
        }

        foreach (var step in proof.Steps)
        {
            byte[] sibling;

            try
            {
                sibling = Convert.FromHexString(step.SiblingHash);
            }
            catch (FormatException)
            {
                return false;
            }

            running = step.SiblingOnRight
                ? InteriorHash(running, sibling)
                : InteriorHash(sibling, running);
        }

        return string.Equals(Convert.ToHexStringLower(running), expectedRoot, StringComparison.Ordinal);
    }

    internal static byte[] LeafHash(string hexHash)
    {
        var bytes = Convert.FromHexString(hexHash);

        return SHA256.HashData([0x00, .. bytes]);
    }

    internal static byte[] InteriorHash(byte[] left, byte[] right)
        => SHA256.HashData([0x01, .. left, .. right]);
}

/// A path from a leaf to the root: the sibling at each level, and which side it sits on.
public sealed record MerkleProof(IReadOnlyList<ProofStep> Steps);

/// One sibling on the way up. `SiblingOnRight` says whether it hashes to the *right* of the running
/// value — getting that order wrong would compute a different, wrong root, so it is carried explicitly.
public sealed record ProofStep(string SiblingHash, bool SiblingOnRight);

/// A signed, published commitment to a ledger at a point in time: the head hash the signature covers,
/// the Merkle root over every record, the record count, and the signature itself.
///
/// **The count is in here on purpose.** A Merkle root alone can be forged into by a duplicated-leaf
/// tree of a different length; binding the count means a verifier checks not just "these records hash
/// to this root" but "there are exactly this many of them", which closes that hole.
public sealed record LedgerCheckpoint(
    string HeadHash,
    string MerkleRoot,
    int RecordCount,
    DateTime PublishedAt,
    byte[] Signature)
{
    /// The bytes the signature actually covers — head, root, and count together, so none can be
    /// swapped independently. This is what `LedgerSigning.Sign`/`Verify` operate on for a checkpoint.
    public string SignedPayload
        => string.Join('\x1e',
            HeadHash,
            MerkleRoot,
            RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PublishedAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
