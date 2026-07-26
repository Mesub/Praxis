using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Modules.Tutor.Domain;
using Praxis.Modules.Tutor.Features.Sessions;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Modules.Tutor.Projections;
using Praxis.Shared;
using KernelHintRung = Praxis.Modules.Kernel.Events.HintRung;

namespace Praxis.Modules.Tutor.Tests;

/// **The tutor gets the real question and its passage, and has nowhere to put an option** (M17).
///
/// Before this milestone `TakeHint` sent a raw GUID as the "concept summary" and the literal words
/// "the current problem" as the question — both placeholders, because the Tutor module owned
/// exactly one port (`IMisconceptionLibrary`) and had no way to reach an item or a concept's
/// summary at all. `ITutorContext` is the seam that closes that gap, and this file proves both
/// halves of the deal: the tutor gets the real stem and passage when a session is bound to an
/// item, and `TutorTurnRequest` has no field an option could ever arrive in.
[Collection(SharedTutor.Name)]
public sealed class TakeHintTests
{
    private readonly TutorDbContext _db;
    private readonly EventStore _events;
    private readonly ProjectionRunner _projections;
    private readonly FixedClock _clock = new();

    private readonly Guid _conceptId = Guid.CreateVersion7();

    /// A fresh learner per test — xunit builds a new instance of this class for every `[Fact]`, so
    /// this Guid is never shared between tests even though the event log underneath it is.
    private readonly Guid LearnerId = Guid.CreateVersion7();

    /// A stand-in item id. `StubTutorContext` returns its configured item regardless of which id is
    /// asked for, so this only needs to be *some* value the session can be bound to.
    private static readonly Guid ItemId = Guid.CreateVersion7();

    public TakeHintTests(TutorFixture fixture)
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
            [new SessionProjection(_db), new AnswerBudgetProjection(_db)]);
    }

    // ── The type is the guarantee ────────────────────────────────────────────

    [Fact]
    public void The_tutor_request_has_no_field_an_option_could_arrive_in()
    {
        // The refusal is enforced by the *type*, not by an instruction in a prompt. If somebody adds
        // `Options` here for a perfectly good reason, this fails before it ships.
        var forbidden = typeof(TutorTurnRequest)
            .GetProperties()
            .Where(p => p.Name.Contains("option", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("choice", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("distractor", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            forbidden.Count == 0,
            $"""
             The tutor's request grew a field the options could travel in: {string.Join(", ", forbidden)}

             The four choices are a closed set containing the answer. A model that can see them can
             eliminate them, and "it is not B or D" is the answer delivered slowly. The passage is
             safe because the learner is reading it; the options are not, and the reason this holds is
             that there is nowhere to put them.
             """);
    }

    // ── The real question, and its passage ───────────────────────────────────

    [Fact]
    public async Task A_session_bound_to_an_item_hands_the_tutor_the_real_question_and_its_passage()
    {
        var ai = new CapturingAiPlane();
        var items = new StubTutorContext(
            item: new TutorItemContext(
                Stem: "Whose roster is the system most likely to get wrong?",
                Passage: "The new roster system goes live on Monday. However, if you swapped a shift..."),
            summary: "Connectives that reverse the claim before them.");

        var session = await StartSessionOn(ItemId);

        await HandlerWith(ai, items).HandleAsync(
            new TakeHint.Command(session.Id, KernelHintRung.Orient, "I think it is everyone"), default);

        Assert.Equal("Whose roster is the system most likely to get wrong?", ai.LastTurn!.ItemStem);
        Assert.Contains("However", ai.LastTurn.Passage);

        // The placeholders are gone: a Socratic tutor working from a GUID and the words "the current
        // problem" was not a tutor.
        Assert.DoesNotContain("the current problem", ai.LastTurn.ItemStem, StringComparison.Ordinal);
        Assert.False(Guid.TryParse(ai.LastTurn.ConceptSummary, out _));
    }

    [Fact]
    public async Task An_unbound_session_still_works_and_sends_no_passage()
    {
        var ai = new CapturingAiPlane();
        var session = await StartSessionOn(itemId: null);

        await HandlerWith(ai, new StubTutorContext(item: null, summary: "A concept summary.")).HandleAsync(
            new TakeHint.Command(session.Id, KernelHintRung.Orient, "something"), default);

        Assert.Null(ai.LastTurn!.Passage);
        Assert.Equal("the current problem", ai.LastTurn.ItemStem);
    }

    // ── The world ────────────────────────────────────────────────────────────

    /// Starts a real session, through the event log, exactly as `StartSession.Handler` would —
    /// so `session.ItemId` on the entity is proved by a replay, not asserted by a shortcut.
    private async Task<Session> StartSessionOn(Guid? itemId)
    {
        var sessionId = Guid.CreateVersion7();

        await _events.AppendAsync(
            LearnerId,
            new SessionStarted(sessionId, PlannedMinutes: 15, itemId),
            _clock.UtcNow,
            _conceptId,
            default);

        await _projections.CatchUpAsync();

        return await _db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
    }

    private TakeHint.Handler HandlerWith(IAiPlane ai, ITutorContext items)
        => new(_db, _events, _projections, ai, new StubMisconceptionLibrary(), items, new FakeUser(LearnerId), _clock);

    /// A concept that declares no misconceptions — irrelevant to what this file is testing, but the
    /// handler needs one to call.
    private sealed class StubMisconceptionLibrary : IMisconceptionLibrary
    {
        public Task<IReadOnlyList<string>> KeysForConceptAsync(Guid conceptId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// A hand-set answer to "what does the tutor know about what the learner is looking at" —
    /// `null` for the item stands in for a session that was never bound to one.
    private sealed class StubTutorContext : ITutorContext
    {
        private readonly TutorItemContext? _item;
        private readonly string? _summary;

        public StubTutorContext(TutorItemContext? item, string? summary)
            => (_item, _summary) = (item, summary);

        public Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default)
            => Task.FromResult(_item);

        public Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default)
            => Task.FromResult(_summary);
    }

    /// Captures the one request that actually reached the AI plane, so a test can assert on the
    /// exact shape the tutor was handed rather than trusting the handler's word for it.
    private sealed class CapturingAiPlane : IAiPlane
    {
        public TutorTurnRequest? LastTurn { get; private set; }

        public Task<Result<TutorTurnResponse>> TutorTurnAsync(
            TutorTurnRequest request, CancellationToken ct = default)
        {
            LastTurn = request;

            return Task.FromResult(Result<TutorTurnResponse>.Success(
                new TutorTurnResponse("a Socratic reply", 0.001)));
        }

        public IAsyncEnumerable<string> TutorTurnStreamAsync(
            TutorTurnRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<CheckClaimResponse>> CheckClaimAsync(
            CheckClaimRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<GradeResponse>> GradeByConceptAsync(
            GradeByConceptRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");

        public Task<Result<ItemResponse>> GenerateItemAsync(
            GenerateItemRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Not part of this slice.");
    }
}
