using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Features.TeachBacks;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Kernel;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Tests;

/// **A teach-back becomes evidence — and the evidence is honest about what it is.**
///
/// The handler grades an explanation and writes two distinct facts: that it was said and scored
/// (`ExplanationRecorded`), and whether it counts as proof (`EvidenceAccepted`/`EvidenceAbstained`).
/// It does **not** decide what that proof is worth — the Twin's `MasteryCredit` does, and that is
/// where the Evidence Ceiling (C-4) lives. These tests prove the *producer* side end to end against a
/// real event store; the belief-and-ceiling side is proved where the Twin projects it.
[Collection(SharedAssessment.Name)]
public sealed class RecordTeachBackTests
{
    private const string Explanation =
        "An inverted index maps each term to the posting list of documents that contain it, so a "
        + "query reads one short list instead of scanning every document.";

    private readonly AssessmentFixture _fixture;

    public RecordTeachBackTests(AssessmentFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_scored_explanation_records_the_fact_and_accepts_it_as_explain_evidence()
    {
        var world = new Harness(_fixture);
        world.Oracle.Grades(new GradeResponse(GradeOutcome.Passed, 0.9, [], "clear", 1, false, false, 0.0));

        var result = await world.Record(Command(world));

        Assert.True(result.IsSuccess);
        Assert.Equal("Passed", result.Value.Outcome);
        Assert.Equal(0.9, result.Value.Score);
        Assert.True(result.Value.RaisedBelief);
        // The ceiling is stated to the learner, always — it is never a surprise.
        Assert.Contains("Evidence Ceiling", result.Value.Ceiling, StringComparison.Ordinal);

        var types = await world.EventTypes();
        Assert.Contains(LearningEventType.ExplanationRecorded, types);
        Assert.Contains(LearningEventType.EvidenceAccepted, types);
        Assert.DoesNotContain(LearningEventType.EvidenceAbstained, types);
    }

    [Fact]
    public async Task A_pending_grade_abstains_and_is_never_quietly_a_pass()
    {
        var world = new Harness(_fixture);
        // The grader declined to decide (R-1). It must not become mastery, now or by a long queue.
        world.Oracle.Grades(new GradeResponse(GradeOutcome.Pending, null, [], "not sure", 2, true, true, 0.0));

        var result = await world.Record(Command(world));

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value.Outcome);
        Assert.False(result.Value.RaisedBelief);

        var types = await world.EventTypes();
        Assert.Contains(LearningEventType.ExplanationRecorded, types);
        Assert.Contains(LearningEventType.EvidenceAbstained, types);
        Assert.DoesNotContain(LearningEventType.EvidenceAccepted, types);
    }

    [Fact]
    public async Task The_speech_is_stripped_before_the_rubric_ever_sees_it()
    {
        var world = new Harness(_fixture);
        world.Oracle.Grades(new GradeResponse(GradeOutcome.Passed, 0.8, [], "ok", 1, false, false, 0.0));

        await world.Record(new RecordTeachBack.Command(
            SessionId: Guid.CreateVersion7(),
            ConceptId: Guid.CreateVersion7(),
            Text: "so um the the index, uh, maps terms to documents you know",
            Seconds: 10));

        var graded = Assert.Single(world.Oracle.Graded);
        // The disfluency and the stutter are gone; the content the rubric grades is the content.
        Assert.DoesNotContain("uh", graded.Response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you know", graded.Response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the the", graded.Response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maps terms to documents", graded.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grader_outage_records_nothing_and_does_not_guess_a_score()
    {
        var world = new Harness(_fixture);
        world.Oracle.GraderFails();

        var result = await world.Record(Command(world));

        Assert.False(result.IsSuccess);
        // Nothing was written against this learner. A false belief from an outage is worse than none.
        Assert.Empty(await world.EventTypes());
    }

    private static RecordTeachBack.Command Command(Harness world) => new(
        SessionId: Guid.CreateVersion7(),
        ConceptId: Guid.CreateVersion7(),
        Text: Explanation,
        Seconds: 45);

    private sealed class Harness
    {
        public FakeOracle Oracle { get; } = new();

        public Guid Learner { get; } = Guid.CreateVersion7();

        private readonly AssessmentFixture _fixture;
        private readonly IEventStore _events;
        private readonly ProjectionRunner _projections;
        private readonly FixedClock _clock = new();

        public Harness(AssessmentFixture fixture)
        {
            _fixture = fixture;

            var db = fixture.CreateContext();
            var kernel = fixture.CreateKernel();

            _events = new EventStore(kernel, _clock);

            // Only Assessment's own projections run here — the Twin's is a different module's, so the
            // accepted-evidence event lands in the log without needing a PoM contract this test has
            // not seeded. The belief math is proved where the Twin projects it.
            _projections = new ProjectionRunner(
                _events,
                kernel,
                _clock,
                NullLogger<ProjectionRunner>.Instance,
                [new AbilityProjection(db), new MisconceptionProjection(db)]);
        }

        public Task<Result<TeachBackResponse>> Record(RecordTeachBack.Command command)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));

            return new RecordTeachBack.Handler(
                    _events, _projections, Oracle, new FakeUser(Learner), _clock)
                .HandleAsync(command, default);
        }

        public async Task<List<LearningEventType>> EventTypes()
        {
            await using var kernel = _fixture.CreateKernel();

            return await kernel.Events
                .Where(e => e.LearnerId == Learner)
                .OrderBy(e => e.Sequence)
                .Select(e => e.Type)
                .ToListAsync();
        }
    }
}

/// The disposition rule, proved without any infrastructure: `Pending` is the only thing that does
/// not count, and it is never rewritten as a pass.
public sealed class TeachBackEvidenceTests
{
    [Theory]
    [InlineData(GradeOutcome.Passed, true)]
    [InlineData(GradeOutcome.Failed, true)]
    [InlineData(GradeOutcome.Pending, false)]
    public void Only_pending_is_not_accepted_as_evidence(GradeOutcome outcome, bool accepted)
        => Assert.Equal(accepted, TeachBackEvidence.IsAccepted(outcome));

    [Fact]
    public void The_rubric_assurance_stays_below_one_a_model_is_never_fully_trusted()
    {
        Assert.True(TeachBackEvidence.RubricAssurance > 0.0);
        Assert.True(TeachBackEvidence.RubricAssurance < 1.0);
    }
}
