using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Features.Diagnostics;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **An answer stops being prose and becomes a choice.**
///
/// A fixed-key item is marked by comparing two integers on the far side of the AI-plane wire — no
/// model, no judgement, no opinion. This file proves the .NET half of that: the shape the learner
/// answered in must match the shape the item was banked in, the option they picked travels to the
/// oracle and nowhere else, and when a distractor names a wrong belief, what lands on the log is the
/// words they actually chose — not the empty claim that rode alongside it.
[Collection(SharedAssessment.Name)]
public sealed class SubmitAnswerTests
{
    private readonly AssessmentDbContext _db;
    private readonly EventStore _events;
    private readonly ProjectionRunner _projections;
    private readonly FixedClock _clock = new();

    private readonly Guid _conceptId = Guid.CreateVersion7();

    /// A fresh learner per test — xunit builds a new instance of this class for every `[Fact]`, so
    /// this Guid is never shared between tests even though the event log underneath it is.
    private readonly Guid LearnerId = Guid.CreateVersion7();

    private readonly Guid SessionId = Guid.CreateVersion7();

    public SubmitAnswerTests(AssessmentFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _db = fixture.CreateContext();

        var kernel = fixture.CreateKernel();

        _events = new EventStore(kernel, _clock);

        _projections = new ProjectionRunner(
            _events,
            kernel,
            _clock,
            NullLogger<ProjectionRunner>.Instance,
            [new AbilityProjection(_db), new MisconceptionProjection(_db)]);
    }

    [Fact]
    public async Task Choosing_the_option_the_oracle_marks_correct_records_a_right_answer()
    {
        var item = await BankFixedKeyItem();
        var ai = new StubAiPlane(ClaimOutcome.Correct, misconception: null);

        var result = await HandlerWith(ai).HandleAsync(
            new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 1),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Correct", result.Value.Outcome);
        Assert.Equal(1, ai.LastCheck!.SelectedOption);
        Assert.Equal(string.Empty, ai.LastCheck.Claim);
    }

    [Fact]
    public async Task A_distractor_records_the_named_belief_with_the_chosen_option_as_evidence()
    {
        var item = await BankFixedKeyItem();
        var ai = new StubAiPlane(ClaimOutcome.Wrong, misconception: "keyword-match-wins");

        await HandlerWith(ai).HandleAsync(
            new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 0),
            default);

        var detected = await EventsFor(LearnerId).OfType<MisconceptionDetected>().SingleAsync();

        Assert.Equal("keyword-match-wins", detected.Name);

        // The evidence is what they actually did: the option they picked, in their words as shown.
        Assert.Equal("Everyone who worked a Saturday shift last month.", detected.Evidence);
    }

    [Fact]
    public async Task Prose_on_a_fixed_key_item_is_refused_and_nothing_is_appended()
    {
        var item = await BankFixedKeyItem();

        var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
            new SubmitAnswer.Command(SessionId, item.Id, Claim: "I think it is B", 0.85, 30, null),
            default);

        Assert.False(result.IsSuccess);
        Assert.Empty(await EventsFor(LearnerId).ToListAsync());
    }

    [Fact]
    public async Task An_option_outside_the_ones_shown_is_refused()
    {
        var item = await BankFixedKeyItem();   // four options

        var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
            new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 4),
            default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task A_selection_on_a_prose_item_is_refused()
    {
        var item = await BankProseItem();

        var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
            new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 0),
            default);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("words", 1)]
    public void An_answer_is_words_or_a_choice_and_never_both_or_neither(string? claim, int? option)
    {
        var result = new SubmitAnswer.Validator().Validate(
            new SubmitAnswer.Command(Guid.NewGuid(), Guid.NewGuid(), claim, 0.7, 10, option));

        Assert.False(result.IsValid);
    }

    // ── The world ────────────────────────────────────────────────────────────

    private async Task<BankedItem> BankFixedKeyItem()
    {
        var id = Guid.CreateVersion7();

        var item = BankedItem.From(
            id,
            _conceptId,
            EvidenceType.Apply,
            stem: "Which sentence best restates the claim in the passage?",
            fingerprint: id.ToString("N"),
            difficulty: 0.0,
            guessing: 0.25,
            format: ItemFormat.MultipleChoice,
            passage: "The memo said the new schedule applies to everyone working weekends.",
            options:
            [
                "Everyone who worked a Saturday shift last month.",
                "Only staff hired after the memo went out.",
                "Nobody — the memo does not name anyone.",
                "The manager who wrote the memo.",
            ]);

        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return item;
    }

    private async Task<BankedItem> BankProseItem()
    {
        var id = Guid.CreateVersion7();

        var item = BankedItem.From(
            id,
            _conceptId,
            EvidenceType.Apply,
            stem: "In your own words, who does the new schedule apply to?",
            fingerprint: id.ToString("N"),
            difficulty: 0.0);

        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return item;
    }

    private SubmitAnswer.Handler HandlerWith(IAiPlane ai)
        => new(_db, _events, _projections, ai, new FakeUser(LearnerId), _clock);

    /// One learner's history, read back through the same deserializer every projection uses — a
    /// helper reading the log by hand would prove nothing about what was actually written.
    private async IAsyncEnumerable<object> EventsFor(Guid learnerId)
    {
        var stream = await _events.ReadStreamAsync(learnerId);

        foreach (var e in stream)
        {
            yield return EventCatalogue.Deserialize(e);
        }
    }

    /// `FakeOracle` implements the whole `IAiPlane` contract — including the same three-words-and-
    /// a-key shape as production — and is `sealed` for exactly that reason: nothing may widen it.
    /// This wraps rather than reimplements it, adding only the single most recent request by name,
    /// which is what these tests want to assert on directly.
    private sealed class StubAiPlane : IAiPlane
    {
        private readonly FakeOracle _oracle = new();

        public StubAiPlane(ClaimOutcome outcome, string? misconception)
            => _oracle.Says(outcome, misconception);

        public CheckClaimRequest? LastCheck
            => _oracle.Asked.Count > 0 ? _oracle.Asked[^1] : null;

        public Task<Result<CheckClaimResponse>> CheckClaimAsync(
            CheckClaimRequest request, CancellationToken ct = default)
            => _oracle.CheckClaimAsync(request, ct);

        public Task<Result<GradeResponse>> GradeByConceptAsync(
            GradeByConceptRequest request, CancellationToken ct = default)
            => _oracle.GradeByConceptAsync(request, ct);

        public Task<Result<ItemResponse>> GenerateItemAsync(
            GenerateItemRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<TutorTurnResponse>> TutorTurnAsync(
            TutorTurnRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public IAsyncEnumerable<string> TutorTurnStreamAsync(
            TutorTurnRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");
    }
}

/// A minimal async LINQ shim over the event log, scoped to exactly the three operations this file's
/// assertions use. Not a general-purpose library — `System.Linq.Async` is not a dependency of this
/// project, and three methods did not earn adding one.
internal static class AsyncEnumerableTestExtensions
{
    public static async IAsyncEnumerable<TResult> OfType<TResult>(this IAsyncEnumerable<object> source)
    {
        await foreach (var item in source)
        {
            if (item is TResult match)
            {
                yield return match;
            }
        }
    }

    public static async Task<TResult> SingleAsync<TResult>(this IAsyncEnumerable<TResult> source)
    {
        await using var e = source.GetAsyncEnumerator();

        if (!await e.MoveNextAsync())
        {
            throw new InvalidOperationException("Sequence contains no matching element.");
        }

        var result = e.Current;

        if (await e.MoveNextAsync())
        {
            throw new InvalidOperationException("Sequence contains more than one matching element.");
        }

        return result;
    }

    public static async Task<List<TResult>> ToListAsync<TResult>(this IAsyncEnumerable<TResult> source)
    {
        var list = new List<TResult>();

        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
