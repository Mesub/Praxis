using Praxis.Modules.Ledger;
using Praxis.Modules.Ledger.Domain;
using Praxis.Modules.Ledger.Features.Records;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Tests;

/// **The endpoint issues a record, and it verifies — the two halves meeting through the handler.**
///
/// The handler holds the snapshot of what was proved and asks the keys to sign; it never touches the
/// private key. What comes out is checkable by anyone against the published key.
public sealed class IssueMyRecordTests
{
    private static readonly DateTime When = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_issued_record_verifies_against_the_published_key()
    {
        var keys = new TestKeys();
        var snapshot = new FakeSnapshot(
        [
            new LedgerIssuer.Held(Guid.CreateVersion7(), "Backpropagation", EvidenceType.Build, GradingMethod.Execution, 0.95, 1.0, 1),
            new LedgerIssuer.Held(Guid.CreateVersion7(), "Attention", EvidenceType.Apply, GradingMethod.KeyMatch, 0.9, 1.0, 2),
        ]);

        var result = await new IssueMyRecord.Handler(snapshot, keys, new FakeUser(), new FixedClock(When))
            .HandleAsync(new IssueMyRecord.Query(), default);

        Assert.True(result.IsSuccess);
        var verified = CompetencyRecords.Verify(result.Value, keys.PublicKey);
        Assert.True(verified.IsValid);
        Assert.Equal(2, verified.VerifiedCompetencies);
    }

    [Fact]
    public async Task A_learner_who_has_proved_nothing_gets_a_valid_empty_record()
    {
        var keys = new TestKeys();

        var result = await new IssueMyRecord.Handler(new FakeSnapshot([]), keys, new FakeUser(), new FixedClock(When))
            .HandleAsync(new IssueMyRecord.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Competencies);
        Assert.True(CompetencyRecords.Verify(result.Value, keys.PublicKey).IsValid);
    }

    private sealed class FakeSnapshot(IReadOnlyList<LedgerIssuer.Held> held) : ILedgerSnapshot
    {
        public Task<IReadOnlyList<LedgerIssuer.Held>> HeldByAsync(Guid learnerId, CancellationToken ct = default)
            => Task.FromResult(held);
    }

    private sealed class TestKeys : ILedgerKeys
    {
        private readonly byte[] _seed;

        public TestKeys()
        {
            var (privateKey, publicKey) = LedgerSigning.GenerateKeyPair();
            _seed = privateKey;
            PublicKey = publicKey;
        }

        public byte[] PublicKey { get; }

        public byte[] Sign(string payload) => LedgerSigning.Sign(payload, _seed);
    }

    private sealed class FakeUser : IUserContext
    {
        public Guid? LearnerId { get; } = Guid.CreateVersion7();

        public bool IsAuthenticated => true;

        public Guid RequireLearnerId() => LearnerId!.Value;
    }

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }
}
