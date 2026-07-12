using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Twin.Persistence;
using Praxis.Modules.Twin.Projections;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Tests;

/// **The Twin, meeting the real event log.**
///
/// Everything proved as a pure function in M3.2–M3.6 has to survive contact with events arriving
/// one at a time, out of a database, with real timestamps.
[Collection(SharedTwin.Name)]
public sealed class TwinProjectionTests
{
    private static readonly DateTime Day1 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    /// A contract shaped like the real pack's: talking gets you close, and never over the line.
    private static readonly MasteryContract Contract = new(
        HoldThreshold: 0.80,
        Clauses:
        [
            new EvidenceClause(EvidenceType.Explain, 0.7, 0.25, 1),
            new EvidenceClause(EvidenceType.Apply, 0.8, 0.60, 2),
            new EvidenceClause(EvidenceType.Build, 1.0, 0.85, 1),
        ]);

    private readonly TwinFixture _twin;

    public TwinProjectionTests(TwinFixture twin) => _twin = twin;

    [Fact]
    public async Task Being_taught_a_concept_changes_nothing_about_what_we_believe()
    {
        // The thesis, at the projection level. A learner can sit through instruction all day; the
        // log records it, and the Twin does not move. ConceptTaught is not even handled.
        var (db, projection) = Twin();

        Assert.DoesNotContain(LearningEventType.ConceptTaught, projection.Handles);
    }

    [Fact]
    public async Task A_correct_answer_raises_belief_but_does_not_confer_mastery()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await projection.ApplyAsync(Event(learner, concept, Day1,
            new AnswerGiven(Guid.CreateVersion7(), Guid.CreateVersion7(), EvidenceType.Apply,
                Correct: true, StatedConfidence: 0.7, Seconds: 90)));

        var state = await State(db, learner, concept);

        Assert.True(state.PKnows > ConceptState.PriorPKnows);

        // Answering is not evidence being *accepted*. Mastery has not moved.
        Assert.Equal(0.0, state.MasteryCredit);
        Assert.False(state.IsHeld);
    }

    [Fact]
    public async Task Accepted_evidence_is_the_only_thing_that_moves_mastery()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await projection.ApplyAsync(Event(learner, concept, Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, Score: 1.0, Assurance: 1.0)));

        var state = await State(db, learner, concept);

        Assert.True(state.IsHeld, $"A build that ran left them at {state.MasteryCredit:F2}.");
        Assert.Equal(1.0, state.Assurance, precision: 6);
        Assert.Equal(1, state.EvidenceCount);

        // And it is now something that can rot.
        Assert.NotNull(state.NextDueAt);
        Assert.True(state.NextDueAt > Day1);
    }

    /// **The Evidence Ceiling, at the projection.** Not a unit test of a function — the real thing,
    /// with events, through a database.
    [Fact]
    public async Task No_quantity_of_explanations_through_the_real_projection_can_confer_mastery()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        for (var i = 0; i < 50; i++)
        {
            await projection.ApplyAsync(Event(learner, concept, Day1.AddHours(i),
                new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Explain, Score: 1.0, Assurance: 1.0)));
        }

        var state = await State(db, learner, concept);

        Assert.False(
            state.IsHeld,
            $"Fifty perfect explanations produced a HELD concept (credit {state.MasteryCredit:F3}). "
            + "A model has just manufactured mastery, and the Ledger is worthless.");

        // It moved — talking is worth something. It just never finishes.
        Assert.True(state.MasteryCredit > 0);
        Assert.True(state.MasteryCredit < Contract.HoldThreshold);

        // And the Twin says plainly that none of this rests on work a model could not fake.
        Assert.Equal(0.0, state.Assurance, precision: 6);
    }

    [Fact]
    public async Task Replaying_the_same_evidence_twice_does_not_inflate_the_learner()
    {
        // Idempotency. A rebuild replays every event; if accepted evidence were counted twice, a
        // rebuild would make every learner in the system better than they are.
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        var evidenceId = Guid.CreateVersion7();
        var e = Event(learner, concept, Day1,
            new EvidenceAccepted(evidenceId, EvidenceType.Apply, 1.0, 1.0));

        await projection.ApplyAsync(e);
        var first = (await State(db, learner, concept)).MasteryCredit;

        await projection.ApplyAsync(e);
        var second = (await State(db, learner, concept)).MasteryCredit;

        Assert.Equal(first, second, precision: 9);
        Assert.Equal(1, await db.Evidence.CountAsync(x => x.EvidenceId == evidenceId));
    }

    [Fact]
    public async Task A_concept_with_no_contract_refuses_to_be_believed_about()
    {
        // The pack loader will not publish a concept without a contract. If one reaches the Twin,
        // the graph was written to outside the gate — and inventing a standard of proof here would
        // be exactly the dishonesty the Ceiling exists to prevent.
        var db = _twin.CreateContext();
        var projection = new TwinProjection(db, new NoContracts());

        var e = Event(Guid.CreateVersion7(), Guid.CreateVersion7(), Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ApplyAsync(e));

        Assert.Contains("standard of proof is unknown", ex.Message, StringComparison.Ordinal);
    }

    // ── Decay ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_held_concept_left_alone_becomes_due()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await projection.ApplyAsync(Event(learner, concept, Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0)));

        var state = await State(db, learner, concept);

        Assert.True(state.IsHeld);
        Assert.NotNull(state.NextDueAt);

        // Due within weeks, not decades. A first proof is not a permanent one.
        var days = (state.NextDueAt!.Value - Day1).TotalDays;
        Assert.InRange(days, 0.5, 60);
    }

    [Fact]
    public async Task A_failed_rep_collapses_stability_and_brings_the_next_one_forward()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await projection.ApplyAsync(Event(learner, concept, Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0)));

        // Weeks later, a successful rep. Stability grows.
        await projection.ApplyAsync(Event(learner, concept, Day1.AddDays(20),
            new RepAnswered(Guid.CreateVersion7(), Correct: true, 0.8, 60, 0, 0)));

        var afterSuccess = (await State(db, learner, concept)).Stability;

        // Then they lose it.
        await projection.ApplyAsync(Event(learner, concept, Day1.AddDays(60),
            new RepAnswered(Guid.CreateVersion7(), Correct: false, 0.9, 90, 0, 0)));

        var afterLapse = (await State(db, learner, concept)).Stability;

        Assert.True(afterLapse < afterSuccess, "Forgetting it made the memory more durable.");
        Assert.True(afterLapse > 0, "A lapse erased the memory entirely. Relearning is not learning.");
    }

    /// **A rep is not special, and this is the line that enforces it** (M8.3).
    ///
    /// The temptation is enormous and entirely reasonable-sounding: the learner has just proved, a
    /// hundred times, that they still know this. Surely it should count for *something*.
    ///
    /// It counts for exactly what it is evidence of — **that the memory is still there** — and not one
    /// thing more. A rep does not tell you they can *do* anything, and only the second thing is
    /// mastery.
    ///
    /// If a rep could confer it, a learner would reach `HOLD` by answering an endless stream of cheap
    /// questions about something they had already proved, and **the Evidence Ceiling (C-4) — the
    /// load-bearing wall of this entire product — would have a door in it marked "review".**
    [Fact]
    public async Task A_hundred_perfect_reps_cannot_confer_mastery()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        // One EXPLAIN: real evidence, and ceiling-bound. It raises belief and cannot cross the line.
        await projection.ApplyAsync(Event(learner, concept, Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Explain, 1.0, 0.8)));

        // **Snapshot the numbers, not the entity.** `State` hands back a *tracked* row, and the
        // projection writes through the same DbContext — so holding on to the object would mean
        // comparing it against itself after the reps had already mutated it, and the test would pass
        // no matter what happened. It cost me a confusing ten minutes; it would cost the next person
        // the same.
        var loneExplain = await State(db, learner, concept);
        var (creditBefore, stabilityBefore) = (loneExplain.MasteryCredit, loneExplain.Stability);

        Assert.False(loneExplain.IsHeld, "The Evidence Ceiling let a lone EXPLAIN through.");

        // Now rep it perfectly, every week, for two years.
        for (var i = 1; i <= 100; i++)
        {
            await projection.ApplyAsync(Event(learner, concept, Day1.AddDays(7 * i),
                new RepAnswered(Guid.CreateVersion7(), Correct: true, 0.95, 20, 0, 0)));
        }

        var after = await State(db, learner, concept);

        Assert.Equal(creditBefore, after.MasteryCredit, tolerance: 1e-9);

        Assert.False(
            after.IsHeld,
            "A hundred passed reps just conferred mastery. The Evidence Ceiling now has a door in it "
            + "marked 'review', and every claim in the Ledger is worthless.");

        // And this is not passing because nothing happened: the memory got **far** more durable —
        // roughly 380× — and PRAXIS still will not say they hold the concept, because they have never
        // once been asked to *do* anything with it.
        Assert.True(
            after.Stability > stabilityBefore * 10,
            $"The reps did not land: stability went {stabilityBefore:F2} -> {after.Stability:F2}. "
            + "This test would prove nothing.");
    }

    // ── Calibration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Confidence_and_correctness_accumulate_into_the_gap()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        // Says 95%. Right half the time.
        for (var i = 0; i < 10; i++)
        {
            await projection.ApplyAsync(Event(learner, concept, Day1.AddHours(i),
                new AnswerGiven(Guid.CreateVersion7(), Guid.CreateVersion7(), EvidenceType.Apply,
                    Correct: i % 2 == 0, StatedConfidence: 0.95, Seconds: 60)));
        }

        var state = await State(db, learner, concept);

        Assert.Equal(10, state.CalibrationSamples);
        Assert.True(state.ConfidenceGap > 0.3, $"The gap was only {state.ConfidenceGap:F2}.");
        Assert.True(state.BrierScore > 0.25);
    }

    // ── Misconceptions ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_misconception_suppresses_belief_in_its_concept()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        await projection.ApplyAsync(Event(learner, concept, Day1,
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Build, 1.0, 1.0)));

        var before = (await State(db, learner, concept)).PKnows;

        await projection.ApplyAsync(Event(learner, concept, Day1.AddHours(1),
            new MisconceptionDetected(Guid.CreateVersion7(),
                "COSINE_IGNORES_NORMALISATION",
                "Answered as though normalisation does not affect cosine similarity.")));

        var after = (await State(db, learner, concept)).PKnows;

        Assert.True(
            after < before,
            "A learner carrying a confidently-held wrong model is worse off than one who knows "
            + "they do not know — because they will not ask. Belief did not move.");

        var m = await db.Misconceptions.SingleAsync(x => x.LearnerId == learner);
        Assert.Equal("cosine_ignores_normalisation", m.Key);
        Assert.True(m.IsActive);
    }

    [Fact]
    public async Task Showing_the_same_wrong_belief_twice_is_one_misconception_seen_twice()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        for (var i = 0; i < 3; i++)
        {
            await projection.ApplyAsync(Event(learner, concept, Day1.AddDays(i),
                new MisconceptionDetected(Guid.CreateVersion7(),
                    "confuses_recall_with_precision", "Used one where the other was asked for.")));
        }

        var all = await db.Misconceptions.Where(m => m.LearnerId == learner).ToListAsync();

        Assert.Single(all);
        Assert.Equal(3, all[0].TimesObserved);
    }

    // ── The rebuild ──────────────────────────────────────────────────────────

    /// **The promise the whole architecture makes.** Destroy everything the Twin believes, replay
    /// the log, and land in exactly the same place.
    [Fact]
    public async Task A_rebuild_reproduces_the_learner_exactly()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        var history = History(learner, concept);

        foreach (var e in history)
        {
            await projection.ApplyAsync(e);
        }

        var before = Snapshot(await State(db, learner, concept));

        // Throw it all away.
        await projection.ResetAsync();
        Assert.Empty(await db.States.Where(s => s.LearnerId == learner).ToListAsync());

        // And recompute it from history alone.
        foreach (var e in history)
        {
            await projection.ApplyAsync(e);
        }

        var after = Snapshot(await State(db, learner, concept));

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_rebuild_erases_a_belief_history_does_not_support()
    {
        var (db, projection) = Twin();
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        // Forge mastery directly into the Twin, as a bad migration or a hand-written row would.
        var forged = ConceptState.Unseen(learner, concept);
        forged.Set(0.99, 0.99, isHeld: true, 100, 5, Day1, Day1.AddDays(100), 0.9, 0.9, 0.01, 50, 1.0, 99);

        db.States.Add(forged);
        await db.SaveChangesAsync();

        // No events at all. History does not support any of this.
        await projection.ResetAsync();

        Assert.Empty(await db.States.Where(s => s.LearnerId == learner).ToListAsync());
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static List<LearningEvent> History(Guid learner, Guid concept) =>
    [
        Event(learner, concept, Day1,
            new AnswerGiven(Guid.CreateVersion7(), Guid.CreateVersion7(), EvidenceType.Apply, true, 0.6, 120)),
        Event(learner, concept, Day1.AddHours(1),
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Explain, 0.9, 1.0)),
        Event(learner, concept, Day1.AddDays(1),
            new MisconceptionDetected(Guid.CreateVersion7(), "cosine_ignores_normalisation", "Said it does not matter.")),
        Event(learner, concept, Day1.AddDays(2),
            new AnswerGiven(Guid.CreateVersion7(), Guid.CreateVersion7(), EvidenceType.Apply, false, 0.9, 200)),
        Event(learner, concept, Day1.AddDays(3),
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 1.0, 0.95)),
        Event(learner, concept, Day1.AddDays(4),
            new EvidenceAccepted(Guid.CreateVersion7(), EvidenceType.Apply, 0.95, 0.95)),
        Event(learner, concept, Day1.AddDays(25),
            new RepAnswered(Guid.CreateVersion7(), true, 0.75, 80, 0, 0)),
    ];

    private static object Snapshot(ConceptState s) => new
    {
        s.PKnows, s.MasteryCredit, s.IsHeld, s.Stability, s.Difficulty,
        s.LastEvidenceAt, s.NextDueAt, s.MeanStatedConfidence, s.MeanCorrectness,
        s.BrierScore, s.CalibrationSamples, s.Assurance, s.EvidenceCount,
    };

    private (TwinDbContext Db, TwinProjection Projection) Twin()
    {
        var db = _twin.CreateContext();
        return (db, new TwinProjection(db, new OneContract(Contract)));
    }

    private static async Task<ConceptState> State(TwinDbContext db, Guid learner, Guid concept)
    {
        db.ChangeTracker.Clear();
        return await db.States.SingleAsync(s => s.LearnerId == learner && s.ConceptId == concept);
    }

    private static LearningEvent Event<T>(Guid learner, Guid concept, DateTime at, T payload)
        where T : IEventPayload
        => LearningEvent.Create(learner, T.EventType, EventCatalogue.Serialize(payload), at, at, concept);

    private sealed class OneContract : IMasteryContracts
    {
        private readonly MasteryContract _contract;

        public OneContract(MasteryContract contract) => _contract = contract;

        public Task<MasteryContract?> ForConceptAsync(Guid conceptId, CancellationToken ct = default)
            => Task.FromResult<MasteryContract?>(_contract);
    }

    private sealed class NoContracts : IMasteryContracts
    {
        public Task<MasteryContract?> ForConceptAsync(Guid conceptId, CancellationToken ct = default)
            => Task.FromResult<MasteryContract?>(null);
    }
}
