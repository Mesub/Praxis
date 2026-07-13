using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **Issue a record, then verify it the way an employer would.** Issuance and verification are two
/// halves of the same guarantee: what PRAXIS signs, anyone can check offline, forever — and nothing
/// that was not signed can be slipped in afterwards.
public sealed class LedgerIssuerTests
{
    private static readonly DateTime When = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static LedgerIssuer.Held Held(string name, EvidenceType type, GradingMethod method, double assurance)
        => new(Guid.CreateVersion7(), name, type, method, Score: 0.9, Assurance: assurance, SourceSequence: 1);

    private static (CompetencyRecord Record, byte[] PublicKey) Issue(params LedgerIssuer.Held[] held)
    {
        var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();

        var record = LedgerIssuer.Issue(
            Guid.CreateVersion7(),
            "A. Learner",
            held,
            payload => LedgerSigning.Sign(payload, privateKey),
            publicKey,
            When);

        return (record, publicKey);
    }

    [Fact]
    public void An_issued_record_verifies_against_the_public_key()
    {
        var (record, publicKey) = Issue(
            Held("Backpropagation", EvidenceType.Build, GradingMethod.Execution, 1.0),
            Held("Attention", EvidenceType.Apply, GradingMethod.KeyMatch, 1.0),
            Held("Tokenization", EvidenceType.Explain, GradingMethod.Rubric, 0.8));

        var result = CompetencyRecords.Verify(record, publicKey);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.VerifiedCompetencies);
    }

    [Fact]
    public void A_model_graded_competency_carries_its_capped_assurance_not_certainty()
    {
        var (record, _) = Issue(Held("Tokenization", EvidenceType.Explain, GradingMethod.Rubric, 0.8));

        // The record does not launder a rubric's opinion into 1.0 (C-6).
        Assert.Equal(0.8, Assert.Single(record.Competencies).Assurance);
    }

    [Fact]
    public void An_empty_ledger_is_a_valid_record_of_nothing_proved()
    {
        var (record, publicKey) = Issue();

        var result = CompetencyRecords.Verify(record, publicKey);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.VerifiedCompetencies);
        Assert.Empty(record.Competencies);
    }

    [Fact]
    public void A_competency_added_after_signing_does_not_prove_out()
    {
        var (record, publicKey) = Issue(
            Held("Backpropagation", EvidenceType.Build, GradingMethod.Execution, 1.0));

        // Doctor the evidence hash — the leaf no longer reaches the signed root.
        var c = record.Competencies[0];
        var chars = c.EvidenceHash.ToCharArray();
        chars[10] = chars[10] == 'a' ? 'b' : 'a';
        var doctored = record with { Competencies = [c with { EvidenceHash = new string(chars) }] };

        Assert.False(CompetencyRecords.Verify(doctored, publicKey).IsValid);
    }

    [Fact]
    public void Selective_disclosure_of_a_subset_still_verifies()
    {
        var (record, publicKey) = Issue(
            Held("Backpropagation", EvidenceType.Build, GradingMethod.Execution, 1.0),
            Held("Attention", EvidenceType.Apply, GradingMethod.KeyMatch, 1.0),
            Held("Tokenization", EvidenceType.Explain, GradingMethod.Rubric, 0.8));

        // Disclose only the first competency; the checkpoint still holds all three, so a subset is a
        // legitimate ceiling, not a forgery.
        var subset = record with { Competencies = [record.Competencies[0]] };

        Assert.True(CompetencyRecords.Verify(subset, publicKey).IsValid);
    }
}
