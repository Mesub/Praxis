using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Praxis.Modules.Ledger.Domain;

/// **The tamper-evidence: every record commits to the one before it, so a single altered byte breaks
/// the chain and names where.**
///
/// This is what turns a table of evidence into a *ledger*. Each entry carries a hash of its own
/// contents **and** the hash of the entry before it. Change one field of one old record — a score
/// nudged up, an assurance quietly raised past its ceiling — and that record's hash no longer matches;
/// every entry after it was computed against the old hash, so the whole tail no longer links up. The
/// forgery is not hidden. It is *loud*, and `Verify` points straight at it.
///
/// ## What "commits to the previous" buys, and what it does not
///
/// A hash chain proves **internal consistency**: given the chain, nobody has edited history without
/// the break being visible. It does **not**, on its own, prove the chain is *authentic* — an attacker
/// who rewrote a record could recompute every hash after it and produce a chain that verifies. That is
/// what the **signature** and the **published Merkle roots** are for (M10.3): the signature makes the
/// head unforgeable, and the published root makes it impossible to rewrite history *without the world
/// noticing you did*. The chain is one of three layers, and this file is honest that it is not the
/// whole story.
///
/// ## Determinism is the whole game
///
/// A hash is only tamper-evidence if the same record always produces the same hash, on every machine,
/// forever. So the serialisation here is **explicit and total** — every field, in a fixed order, with
/// a fixed culture, joined by a delimiter that cannot appear inside a field. No JSON (property order
/// and float formatting drift between runtimes), no `GetHashCode` (randomised per process), no
/// `ToString()` on a double (culture-dependent). If two runs of PRAXIS ever disagreed on a record's
/// hash, every export would be unverifiable, so this is written to never do that.
public static class LedgerChain
{
    /// The hash the very first entry links to. A well-known constant, so an empty ledger and a
    /// one-entry ledger both have an unambiguous, checkable shape.
    public const string GenesisHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    /// Seal a piece of evidence into a chain entry, linked to the previous entry's hash.
    public static LedgerEntry Append(Evidence evidence, string previousHash)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrEmpty(previousHash);

        var entryHash = Hash(evidence, previousHash);

        return new LedgerEntry(evidence, previousHash, entryHash);
    }

    /// Seal the first entry of a ledger, linked to genesis.
    public static LedgerEntry AppendFirst(Evidence evidence) => Append(evidence, GenesisHash);

    /// **The hash of one entry: its contents, and the previous hash, together.**
    ///
    /// Including the previous hash is what makes it a *chain* — an entry's identity depends on the
    /// entire history behind it, so you cannot lift a valid entry out of one ledger and drop it into
    /// another.
    public static string Hash(Evidence e, string previousHash)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Preimage(e, previousHash))));

    /// **The exact bytes the hash commits to** — every field, in a fixed order, with fixed formats and
    /// a delimiter (`\x1e`, ASCII record separator) that cannot occur in a GUID, a number, or an enum
    /// name. *The serialisation **is** the commitment*: change it and every previously-published hash
    /// breaks. Exposed so the byte-mutation suite (M10.5) can prove there is no byte in here a forger
    /// could flip without changing the hash — that nothing committed is un-covered.
    internal static string Preimage(Evidence e, string previousHash)
    {
        ArgumentNullException.ThrowIfNull(e);

        return string.Join('\x1e',
            e.Id.ToString("N"),
            e.LearnerId.ToString("N"),
            e.ConceptId.ToString("N"),
            ((int)e.Type).ToString(CultureInfo.InvariantCulture),
            ((int)e.GradingMethod).ToString(CultureInfo.InvariantCulture),
            e.Score.ToString("R", CultureInfo.InvariantCulture),
            e.Assurance.ToString("R", CultureInfo.InvariantCulture),
            e.SourceSequence.ToString(CultureInfo.InvariantCulture),
            e.ProducedAt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            previousHash);
    }

    /// **Walk the chain and prove it has not been tampered with — or name the first link that fails.**
    ///
    /// Two things have to hold at every entry: its stored hash must match a fresh recomputation of its
    /// contents (nobody edited the *record*), and the previous-hash it carries must equal the actual
    /// hash of the entry before it (nobody edited the *order*, or spliced something in). The first
    /// entry links to genesis.
    ///
    /// It reports the **first** break, by design: once the chain is broken, everything after it is
    /// suspect anyway, and a wall of failures is less useful than one finger pointed at where the lie
    /// begins.
    public static ChainVerification Verify(IReadOnlyList<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var expectedPrevious = GenesisHash;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!string.Equals(entry.PreviousHash, expectedPrevious, StringComparison.Ordinal))
            {
                // The link is wrong: this entry claims to follow something other than what actually
                // precedes it. An insertion, a deletion, or a reordering.
                return ChainVerification.BrokenAt(i,
                    $"Entry {i} links to {Short(entry.PreviousHash)}, but the entry before it hashes "
                    + $"to {Short(expectedPrevious)}. The chain has been reordered, or a record was "
                    + "inserted or removed here.");
            }

            var recomputed = Hash(entry.Evidence, entry.PreviousHash);

            if (!string.Equals(entry.Hash, recomputed, StringComparison.Ordinal))
            {
                // The contents were edited after sealing: the stored hash no longer matches the record.
                return ChainVerification.BrokenAt(i,
                    $"Entry {i} does not hash to its stored value — its contents were altered after "
                    + "it was sealed. This record has been tampered with.");
            }

            expectedPrevious = entry.Hash;
        }

        return ChainVerification.Intact(entries.Count);
    }

    private static string Short(string hash)
        => hash.Length <= 12 ? hash : hash[..12] + "…";
}

/// One sealed link in the chain: the evidence, the hash it links back to, and its own hash.
public sealed record LedgerEntry(Evidence Evidence, string PreviousHash, string Hash);

/// The result of walking a chain: intact, or broken at a named entry.
public sealed record ChainVerification(bool IsIntact, int Length, int? BrokenAtIndex, string? Reason)
{
    public static ChainVerification Intact(int length) => new(true, length, null, null);

    public static ChainVerification BrokenAt(int index, string reason)
        => new(false, index, index, reason);
}
