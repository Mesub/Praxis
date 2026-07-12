using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **The tamper-evidence.** A clean chain verifies; a single altered record, or one entry moved,
/// inserted, or removed, breaks it — loudly, and pointed at where the lie begins.
public sealed class LedgerChainTests
{
    private static readonly DateTime When = new(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Evidence AnyEvidence(double score = 1.0, int seq = 1)
        => Evidence.Record(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            EvidenceType.Build, GradingMethod.Execution, score, assurance: 1.0,
            sourceSequence: seq, When).Value;

    /// A chain of `count` distinct entries, correctly linked from genesis.
    private static List<LedgerEntry> Chain(int count)
    {
        var entries = new List<LedgerEntry>();
        var previous = LedgerChain.GenesisHash;

        for (var i = 0; i < count; i++)
        {
            var entry = LedgerChain.Append(AnyEvidence(seq: i + 1), previous);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    // ── A clean chain verifies ───────────────────────────────────────────────

    [Fact]
    public void An_intact_chain_verifies()
    {
        var result = LedgerChain.Verify(Chain(20));

        Assert.True(result.IsIntact);
        Assert.Equal(20, result.Length);
        Assert.Null(result.BrokenAtIndex);
    }

    [Fact]
    public void An_empty_chain_is_trivially_intact()
    {
        Assert.True(LedgerChain.Verify([]).IsIntact);
    }

    /// The first entry links to genesis — a well-known constant, so an empty ledger and a one-entry
    /// ledger both have a checkable shape.
    [Fact]
    public void The_first_entry_links_to_genesis()
    {
        var first = LedgerChain.AppendFirst(AnyEvidence());

        Assert.Equal(LedgerChain.GenesisHash, first.PreviousHash);
        Assert.True(LedgerChain.Verify([first]).IsIntact);
    }

    // ── Every kind of tamper is caught, and named ────────────────────────────

    /// **Edit one record and the chain breaks at exactly that record.** Its stored hash no longer
    /// matches its contents.
    [Fact]
    public void Altering_a_records_contents_breaks_the_chain_at_that_record()
    {
        var chain = Chain(10);

        // Simulate an attacker nudging entry 4's **score** up while leaving everything else — the id,
        // the concept, even the sealed hash — untouched. This is the realistic forgery, and it must
        // still break, which is only possible if the score is part of what the hash commits to.
        var original = chain[4].Evidence;
        chain[4] = chain[4] with { Evidence = original with { Score = original.Score - 0.4 } };

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(4, result.BrokenAtIndex);
        Assert.Contains("tampered with", result.Reason!, StringComparison.Ordinal);
    }

    /// **Alter the stored hash itself** and it is caught the same way — a hash that does not match the
    /// contents it claims to seal.
    [Fact]
    public void Altering_a_stored_hash_breaks_the_chain_there()
    {
        var chain = Chain(6);

        chain[2] = chain[2] with { Hash = new string('a', 64) };

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);

        // The bad hash is detected either at entry 2 (its contents no longer hash to it) or at entry 3
        // (whose previous-link no longer matches). Either way, it is named and it is early.
        Assert.True(result.BrokenAtIndex is 2 or 3);
    }

    /// **Remove an entry** and the chain no longer links up where the gap is.
    [Fact]
    public void Deleting_an_entry_breaks_the_link()
    {
        var chain = Chain(8);

        chain.RemoveAt(3);

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(3, result.BrokenAtIndex);
        Assert.Contains("removed", result.Reason!, StringComparison.Ordinal);
    }

    /// **Splice a forged entry in** and the entry after it no longer links to what actually precedes
    /// it.
    [Fact]
    public void Inserting_a_forged_entry_breaks_the_link()
    {
        var chain = Chain(8);

        // A perfectly-formed entry — its own hash is internally valid — dropped into the middle.
        var forged = LedgerChain.Append(AnyEvidence(seq: 99), chain[3].Hash);
        chain.Insert(4, forged);

        var result = LedgerChain.Verify(chain);

        Assert.False(result.IsIntact);

        // The forged entry links correctly to entry 3, but the *original* entry 4 (now at index 5)
        // links to the old entry-3 hash, not to the forged entry — so the break surfaces at 5.
        Assert.Equal(5, result.BrokenAtIndex);
    }

    /// **Reorder two entries** and the links cross.
    [Fact]
    public void Reordering_entries_breaks_the_chain()
    {
        var chain = Chain(8);

        (chain[2], chain[5]) = (chain[5], chain[2]);

        Assert.False(LedgerChain.Verify(chain).IsIntact);
    }

    // ── Determinism is the whole game ────────────────────────────────────────

    /// **The same record always hashes the same way** — on every machine, every run. If it did not,
    /// every export would be unverifiable.
    [Fact]
    public void Hashing_is_deterministic()
    {
        var e = AnyEvidence(score: 0.73, seq: 7);

        var a = LedgerChain.Hash(e, LedgerChain.GenesisHash);
        var b = LedgerChain.Hash(e, LedgerChain.GenesisHash);

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);   // SHA-256, lowercase hex
    }

    /// **An entry's hash depends on the whole history behind it**, so a valid entry cannot be lifted
    /// out of one ledger and dropped into another — its previous-hash would not match.
    [Fact]
    public void An_entry_cannot_be_transplanted_into_a_different_chain()
    {
        var e = AnyEvidence();

        var inChainA = LedgerChain.Hash(e, "aaaa");
        var inChainB = LedgerChain.Hash(e, "bbbb");

        Assert.NotEqual(inChainA, inChainB);
    }

    /// **Every field is committed to** — nudge any one of them on an otherwise-identical record and
    /// the hash moves. This is what makes editing a score, an assurance, or a grading method visible.
    /// Tested one field at a time on the *same* record, so a field silently dropped from the hash
    /// fails here rather than hiding behind a different id.
    [Fact]
    public void Every_field_of_a_record_changes_its_hash()
    {
        var e = AnyEvidence(score: 0.60, seq: 7);
        var baseline = LedgerChain.Hash(e, LedgerChain.GenesisHash);

        Assert.NotEqual(baseline, LedgerChain.Hash(e with { Score = 0.95 }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { Assurance = 0.5 }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { SourceSequence = 99 }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { ConceptId = Guid.CreateVersion7() }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { Type = EvidenceType.Predict }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { GradingMethod = GradingMethod.Reality }, LedgerChain.GenesisHash));
        Assert.NotEqual(baseline, LedgerChain.Hash(e with { ProducedAt = When.AddSeconds(1) }, LedgerChain.GenesisHash));
    }
}
