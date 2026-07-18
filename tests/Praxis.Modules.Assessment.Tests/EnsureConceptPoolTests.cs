using Microsoft.EntityFrameworkCore;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Features.Diagnostics;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **A thin pool is topped up with items whose answers this process never saw.**
///
/// `EnsureConceptPool` never reads an answer, never writes one, and never touches the event log — it
/// only decides *how many more items the bank needs* and asks the generator for exactly that many. So
/// these tests prove counting, not belief: the floor is respected, "unseen" is scoped to the learner
/// asking, the bank's fingerprints travel so a duplicate stem never gets far enough to hit the unique
/// index, the declared wrong beliefs travel unmodified, and a mid-batch failure keeps whatever real
/// items already landed rather than discarding them.
[Collection(SharedAssessment.Name)]
public sealed class EnsureConceptPoolTests
{
    private readonly AssessmentFixture _fixture;

    public EnsureConceptPoolTests(AssessmentFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_thin_pool_is_topped_up_to_the_floor()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        await world.BankAsync(conceptId, Fingerprint());

        var responses = new[]
        {
            Generated("oracle-1", 0.5, Fingerprint(), "thinks-tf-is-linear"),
            Generated("oracle-2", 0.5, Fingerprint(), null),
            Generated("oracle-3", 0.5, Fingerprint(), "thinks-tf-is-linear"),
            Generated("oracle-4", 0.5, Fingerprint(), null),
        };
        world.Ai.Returns(responses);

        var result = await world.Run(conceptId);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Generated);
        Assert.Equal(5, result.Value.PoolSize);
        Assert.Equal(4, world.Ai.Captured.Count);

        var rows = await world.ItemsFor(conceptId);
        Assert.Equal(5, rows.Count);

        foreach (var response in responses)
        {
            var expected = response.Value;
            var row = Assert.Single(rows, r => r.Fingerprint == expected.Fingerprint);

            // Difficulty mapped back to logits: generator 0..1 → IRT logits, (d * 6) - 3.
            // 0.5 → 0.0.
            Assert.Equal(0.0, row.Difficulty, tolerance: 1e-9);
            Assert.Equal(expected.Id, row.OracleId);
            Assert.Equal(expected.TargetsMisconception, row.TargetsMisconception);
        }
    }

    [Fact]
    public async Task A_full_pool_generates_nothing()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        for (var i = 0; i < EnsureConceptPool.PoolFloor; i++)
        {
            await world.BankAsync(conceptId, Fingerprint());
        }

        var result = await world.Run(conceptId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Generated);
        Assert.Equal(EnsureConceptPool.PoolFloor, result.Value.PoolSize);
        Assert.Empty(world.Ai.Captured);
    }

    [Fact]
    public async Task What_the_learner_has_answered_is_not_unseen()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        var banked = new List<BankedItem>();
        for (var i = 0; i < 5; i++)
        {
            banked.Add(await world.BankAsync(conceptId, Fingerprint()));
        }

        // 3 of the 5 banked items have already been answered by this learner.
        foreach (var item in banked.Take(3))
        {
            await world.MarkSeenAsync(conceptId, item.Id);
        }

        world.Ai.Returns(
            Generated("oracle-1", 0.5, Fingerprint()),
            Generated("oracle-2", 0.5, Fingerprint()),
            Generated("oracle-3", 0.5, Fingerprint()));

        var result = await world.Run(conceptId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Generated);
        Assert.Equal(3, world.Ai.Captured.Count);
    }

    [Fact]
    public async Task The_generator_is_handed_the_banks_fingerprints()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        var fingerprintA = Fingerprint();
        var fingerprintB = Fingerprint();
        await world.BankAsync(conceptId, fingerprintA);
        await world.BankAsync(conceptId, fingerprintB);

        world.Ai.Returns(
            Generated("oracle-1", 0.5, Fingerprint()),
            Generated("oracle-2", 0.5, Fingerprint()),
            Generated("oracle-3", 0.5, Fingerprint()));

        await world.Run(conceptId);

        Assert.Equal(3, world.Ai.Captured.Count);

        // Every banked fingerprint, in every captured request — novelty is checked against the whole
        // bank, not just the fingerprints known when the loop started.
        foreach (var captured in world.Ai.Captured)
        {
            Assert.Contains(fingerprintA, captured.AlreadySeen);
            Assert.Contains(fingerprintB, captured.AlreadySeen);
        }
    }

    [Fact]
    public async Task The_declared_wrong_beliefs_travel()
    {
        var misconceptions = new Dictionary<string, string>
        {
            ["thinks-tf-is-linear"] = "Believes TF-IDF weight grows linearly with term frequency.",
        };
        var world = new Harness(_fixture, Intel(misconceptions));
        var conceptId = Guid.CreateVersion7();

        // 4 banked, unseen, so exactly 1 generate call is needed.
        for (var i = 0; i < 4; i++)
        {
            await world.BankAsync(conceptId, Fingerprint());
        }

        world.Ai.Returns(Generated("oracle-1", 0.5, Fingerprint()));

        await world.Run(conceptId);

        var captured = Assert.Single(world.Ai.Captured);
        Assert.Equal(misconceptions, captured.KnownMisconceptions);
    }

    /// The generator's difficulty is an *echo* of what we asked for, on a 0–1 scale. A buggy or
    /// adversarial AI plane echoing something outside that scale must not be able to write a wild
    /// IRT logit into the bank — the estimator would trust it, and every theta it touched with it.
    [Fact]
    public async Task An_out_of_scale_difficulty_echo_is_clamped_before_it_becomes_a_logit()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        // 3 banked, unseen — exactly 2 generate calls needed.
        for (var i = 0; i < 3; i++)
        {
            await world.BankAsync(conceptId, Fingerprint());
        }

        world.Ai.Returns(
            Generated("oracle-high", 1.8, Fingerprint()),
            Generated("oracle-low", -0.2, Fingerprint()));

        var result = await world.Run(conceptId);

        Assert.True(result.IsSuccess);

        var rows = await world.ItemsFor(conceptId);

        // Clamped to the ends of the scale, then mapped: 1 → +3 logits, 0 → -3 logits.
        Assert.Equal(3.0, Assert.Single(rows, r => r.OracleId == "oracle-high").Difficulty, 1e-9);
        Assert.Equal(-3.0, Assert.Single(rows, r => r.OracleId == "oracle-low").Difficulty, 1e-9);
    }

    [Fact]
    public async Task A_generation_failure_banks_nothing_and_carries_the_failure()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        world.Ai.Returns(Result<ItemResponse>.Invalid("The generator could not be reached."));

        var result = await world.Run(conceptId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Single(world.Ai.Captured);
        Assert.Empty(await world.ItemsFor(conceptId));
    }

    [Fact]
    public async Task A_partial_failure_keeps_the_real_items()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        world.Ai.Returns(
            Generated("oracle-1", 0.5, Fingerprint()),
            Generated("oracle-2", 0.5, Fingerprint()),
            Result<ItemResponse>.Invalid("The generator could not be reached."));

        var result = await world.Run(conceptId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Generated);
        Assert.Equal(3, world.Ai.Captured.Count);
        Assert.Equal(2, (await world.ItemsFor(conceptId)).Count);
    }

    /// `AlreadySeen` means the real plane never hands back a banked stem — but that guarantee lives
    /// on the far side of the wire, and this handler must stay honest when the far side is not: a
    /// misbehaving plane echoing a fingerprint the bank already holds is a *conflict* the caller can
    /// retry, not an unhandled exception surfacing as a 500.
    [Fact]
    public async Task A_fingerprint_the_bank_already_holds_is_a_conflict_not_a_crash()
    {
        var world = new Harness(_fixture);
        var conceptId = Guid.CreateVersion7();

        var alreadyBanked = Fingerprint();
        await world.BankAsync(conceptId, alreadyBanked);

        world.Ai.Returns(
            Generated("oracle-1", 0.5, Fingerprint()),
            Generated("oracle-2", 0.5, Fingerprint()),
            Generated("oracle-3", 0.5, Fingerprint()),
            Generated("oracle-dup", 0.5, alreadyBanked));

        var result = await world.Run(conceptId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, result.Status);

        // And the save was all-or-nothing: the three real items did not land alongside the dup,
        // because the one SaveChangesAsync is one transaction.
        Assert.Single(await world.ItemsFor(conceptId));
    }

    [Fact]
    public async Task An_unknown_concept_is_not_found()
    {
        var world = new Harness(_fixture, intel: null);

        var result = await world.Run(Guid.CreateVersion7());

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Empty(world.Ai.Captured);
    }

    private static string Fingerprint() => $"fp-{Guid.NewGuid():N}";

    private static ConceptIntel Intel(IReadOnlyDictionary<string, string>? misconceptions = null)
        => new(
            Summary: "An inverted index maps terms to the documents that contain them.",
            DifficultyTarget: 0.5,
            DeclaredMisconceptions: misconceptions ?? new Dictionary<string, string>());

    private static Result<ItemResponse> Generated(
        string oracleId, double difficulty, string fingerprint, string? targetsMisconception = null)
        => Result<ItemResponse>.Success(new ItemResponse(
            Id: oracleId,
            ConceptId: "concept",
            EvidenceType: EvidenceType.Apply,
            Stem: "A generated stem nobody in this process has read the answer to.",
            Difficulty: difficulty,
            Fingerprint: fingerprint,
            TargetsMisconception: targetsMisconception));

    private sealed class Harness
    {
        private readonly AssessmentFixture _fixture;
        private readonly ConceptIntel? _intel;

        public FakeGenerator Ai { get; } = new();

        public Guid Learner { get; } = Guid.CreateVersion7();

        /// The default intel — a concept the Graph knows about. To test the "unknown concept"
        /// path, pass `intel: null` explicitly to the other constructor rather than relying on
        /// coalescing, which would erase the distinction this harness exists to test.
        public Harness(AssessmentFixture fixture) : this(fixture, Intel()) { }

        public Harness(AssessmentFixture fixture, ConceptIntel? intel)
        {
            _fixture = fixture;
            _intel = intel;
        }

        public async Task<BankedItem> BankAsync(
            Guid conceptId, string fingerprint, string? targetsMisconception = null)
        {
            await using var db = _fixture.CreateContext();

            var item = BankedItem.From(
                Guid.CreateVersion7(),
                conceptId,
                EvidenceType.Apply,
                stem: $"stem-{fingerprint}",
                fingerprint: fingerprint,
                difficulty: 0.0,
                targetsMisconception: targetsMisconception);

            db.Items.Add(item);
            await db.SaveChangesAsync();

            return item;
        }

        public async Task MarkSeenAsync(Guid conceptId, Guid itemId)
        {
            await using var db = _fixture.CreateContext();

            db.Responses.Add(RecordedResponse.From(
                Guid.CreateVersion7(),
                Learner,
                conceptId,
                Guid.CreateVersion7(),
                itemId,
                correct: true,
                statedConfidence: 0.5,
                seconds: 10,
                occurredAt: DateTime.UtcNow));

            await db.SaveChangesAsync();
        }

        public async Task<Result<EnsureConceptPool.PoolResponse>> Run(Guid conceptId)
        {
            await using var db = _fixture.CreateContext();

            var handler = new EnsureConceptPool.Handler(
                db, Ai, new FakeConceptIntel(_intel), new FakeUser(Learner));

            return await handler.HandleAsync(new EnsureConceptPool.Command(conceptId), default);
        }

        public async Task<List<BankedItem>> ItemsFor(Guid conceptId)
        {
            await using var db = _fixture.CreateContext();

            return await db.Items.AsNoTracking()
                .Where(i => i.ConceptId == conceptId)
                .ToListAsync();
        }
    }

    /// The port, stubbed for exactly the one call this slice makes — modelled on `FakeOracle` in this
    /// same test project, and bound by the same shape the real client is: `ItemResponse` has no
    /// answer field, so even a fake written to leak one would have nowhere to put it.
    private sealed class FakeGenerator : IAiPlane
    {
        private readonly Queue<Result<ItemResponse>> _script = new();

        public List<GenerateItemRequest> Captured { get; } = [];

        public void Returns(params Result<ItemResponse>[] results)
        {
            foreach (var result in results)
            {
                _script.Enqueue(result);
            }
        }

        public Task<Result<ItemResponse>> GenerateItemAsync(
            GenerateItemRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            Captured.Add(request);

            if (_script.Count == 0)
            {
                throw new InvalidOperationException("FakeGenerator ran out of scripted responses.");
            }

            return Task.FromResult(_script.Dequeue());
        }

        public Task<Result<CheckClaimResponse>> CheckClaimAsync(
            CheckClaimRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<GradeResponse>> GradeByConceptAsync(
            GradeByConceptRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<TutorTurnResponse>> TutorTurnAsync(
            TutorTurnRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public IAsyncEnumerable<string> TutorTurnStreamAsync(
            TutorTurnRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");
    }

    private sealed class FakeConceptIntel : IConceptIntel
    {
        private readonly ConceptIntel? _intel;

        public FakeConceptIntel(ConceptIntel? intel) => _intel = intel;

        public Task<ConceptIntel?> ForAsync(Guid conceptId, CancellationToken ct = default)
            => Task.FromResult(_intel);
    }
}
