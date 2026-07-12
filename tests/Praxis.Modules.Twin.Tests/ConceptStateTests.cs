using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Domain;

namespace Praxis.Modules.Twin.Tests;

public sealed class ConceptStateTests
{
    [Fact]
    public void An_unseen_concept_starts_from_believing_you_know_nothing()
    {
        // PRAXIS does not assume competence and wait to be disproved. It assumes nothing and makes
        // you show it.
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal(ConceptState.PriorPKnows, state.PKnows);
        Assert.Equal(0.0, state.MasteryCredit);
        Assert.False(state.IsHeld);
        Assert.Equal(0, state.EvidenceCount);

        // And it does not pretend to know how *you* find it, either.
        Assert.Equal(ConceptState.DefaultDifficulty, state.Difficulty);

        // Nothing has decayed yet, because nothing was ever learned.
        Assert.Null(state.LastEvidenceAt);
        Assert.Null(state.NextDueAt);
    }

    [Fact]
    public void Assurance_starts_at_zero_because_nothing_has_been_proved()
    {
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal(0.0, state.Assurance);
    }

    [Fact]
    public void Calibration_on_no_samples_is_not_reported_as_knowledge()
    {
        // Brier on n=0 is not "perfectly calibrated". It is unknown, and the UI must be able to
        // tell the difference.
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal(0, state.CalibrationSamples);
    }

    [Fact]
    public void The_confidence_gap_is_computed_never_stored()
    {
        // A stored copy of a derived value is a second version of the truth, and it will drift.
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        state.Set(
            pKnows: 0.5, masteryCredit: 0.4, isHeld: false,
            stability: 3, difficulty: 5,
            lastEvidenceAt: null, nextDueAt: null,
            meanStatedConfidence: 0.90,   // "I've got this"
            meanCorrectness: 0.40,        // they have not
            brierScore: 0.30, calibrationSamples: 5,
            assurance: 0.5, evidenceCount: 5);

        // The most dangerous state a learner can be in: they will not study it, because they think
        // they already know it.
        Assert.Equal(0.50, state.ConfidenceGap, precision: 10);
    }

    [Fact]
    public void A_learner_can_be_better_than_they_believe()
    {
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        state.Set(0.8, 0.8, true, 20, 4, null, null,
            meanStatedConfidence: 0.40,
            meanCorrectness: 0.85,
            brierScore: 0.20, calibrationSamples: 10, assurance: 0.9, evidenceCount: 10);

        // Negative: they hold it and do not know they hold it, and are wasting their time on it.
        Assert.True(state.ConfidenceGap < 0);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void Beliefs_are_probabilities_and_cannot_escape_zero_to_one(double given, double expected)
    {
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        state.Set(given, given, false, 0, 5, null, null, given, given, 0.2, 1, given, 1);

        Assert.Equal(expected, state.PKnows);
        Assert.Equal(expected, state.MasteryCredit);
        Assert.Equal(expected, state.Assurance);
    }

    [Theory]
    [InlineData(0.0, 1.0)]     // FSRS difficulty is 1–10
    [InlineData(99.0, 10.0)]
    public void Difficulty_stays_on_the_fsrs_scale(double given, double expected)
    {
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        state.Set(0.5, 0.5, false, 1, given, null, null, 0.5, 0.5, 0.2, 1, 0.5, 1);

        Assert.Equal(expected, state.Difficulty);
    }

    [Fact]
    public void Stability_cannot_be_negative()
    {
        // Negative stability would mean a memory that gets stronger by being ignored.
        var state = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        state.Set(0.5, 0.5, false, stability: -10, difficulty: 5, null, null, 0.5, 0.5, 0.2, 1, 0.5, 1);

        Assert.Equal(0.0, state.Stability);
    }
}

public sealed class MisconceptionTests
{
    [Fact]
    public void A_misconception_is_a_named_wrong_belief_not_a_wrong_answer()
    {
        // The difference between a quiz app and a Twin. A quiz app knows your score; this knows
        // what is wrong with you, by name.
        var m = Misconception.Observed(
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            key: "COSINE_IGNORES_NORMALISATION",
            statement: "Cosine similarity gives the same answer whether or not I normalised the vectors.",
            at: DateTime.UtcNow);

        Assert.Equal("cosine_ignores_normalisation", m.Key);
        Assert.Contains("normalised", m.Statement, StringComparison.Ordinal);
        Assert.True(m.IsActive);
        Assert.Equal(1, m.TimesObserved);
    }

    [Fact]
    public void Being_told_you_are_wrong_does_not_retire_a_misconception()
    {
        // It is retired when the learner stops answering as though they hold it — the same standard
        // the rest of the product applies to everything else. Explanations are not evidence.
        var m = Observed();

        m.ObserveAgain(DateTime.UtcNow);

        Assert.True(m.IsActive);
        Assert.Equal(2, m.TimesObserved);
    }

    [Fact]
    public void One_contradiction_is_not_enough_to_retire_a_misconception()
    {
        // Once could be luck. A Twin that forgives too easily is a Twin that keeps telling the
        // learner they are fine.
        var m = Observed();

        m.Contradict();

        Assert.True(m.IsActive);
        Assert.Equal(1, m.TimesContradicted);
    }

    [Fact]
    public void Two_clean_contradictions_retire_it()
    {
        var m = Observed();

        m.Contradict();
        m.Contradict();

        Assert.False(m.IsActive);
        Assert.Equal(Misconception.ContradictionsToRetire, m.TimesContradicted);
    }

    [Fact]
    public void A_retired_misconception_comes_back_if_the_learner_shows_it_again()
    {
        // Misconceptions relapse. Pretending otherwise is how a learner ships the same bug twice.
        var m = Observed();

        m.Contradict();
        m.Contradict();
        Assert.False(m.IsActive);

        m.ObserveAgain(DateTime.UtcNow);

        Assert.True(m.IsActive);
    }

    private static Misconception Observed() => Misconception.Observed(
        Guid.CreateVersion7(), Guid.CreateVersion7(),
        "confuses_recall_with_precision",
        "Recall and precision are two words for the same thing.",
        new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));
}

[Collection(SharedTwin.Name)]
public sealed class TwinPersistenceTests
{
    private readonly TwinFixture _twin;

    public TwinPersistenceTests(TwinFixture twin) => _twin = twin;

    [Fact]
    public async Task A_belief_survives_a_round_trip()
    {
        await using var db = _twin.CreateContext();

        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();
        var at = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

        var state = ConceptState.Unseen(learner, concept);
        state.Set(0.72, 0.81, true, 14.5, 6.2, at, at.AddDays(21), 0.65, 0.80, 0.18, 12, 0.95, 12);

        db.States.Add(state);
        await db.SaveChangesAsync();

        await using var fresh = _twin.CreateContext();
        var stored = await fresh.States.SingleAsync(s => s.LearnerId == learner);

        Assert.Equal(0.72, stored.PKnows, precision: 6);
        Assert.True(stored.IsHeld);
        Assert.Equal(14.5, stored.Stability, precision: 6);
        Assert.Equal(at.AddDays(21), stored.NextDueAt);
        Assert.Equal(0.95, stored.Assurance, precision: 6);

        // Computed on read, not stored.
        Assert.Equal(-0.15, stored.ConfidenceGap, precision: 6);
    }

    [Fact]
    public async Task The_twin_cannot_hold_two_opinions_about_the_same_person_and_concept()
    {
        await using var db = _twin.CreateContext();

        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        db.States.Add(ConceptState.Unseen(learner, concept));
        await db.SaveChangesAsync();

        db.States.Add(ConceptState.Unseen(learner, concept));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_wrong_idea_is_one_misconception_observed_many_times()
    {
        await using var db = _twin.CreateContext();

        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();
        var at = DateTime.UtcNow;

        db.Misconceptions.Add(Misconception.Observed(
            learner, concept, "cosine_ignores_normalisation", "Normalisation does not matter.", at));

        await db.SaveChangesAsync();

        db.Misconceptions.Add(Misconception.Observed(
            learner, concept, "cosine_ignores_normalisation", "Normalisation does not matter.", at));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
