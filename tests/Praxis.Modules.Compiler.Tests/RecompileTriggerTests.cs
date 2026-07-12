using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Kernel.Domain;

namespace Praxis.Modules.Compiler.Tests;

/// **When is a plan out of date?**
///
/// Wrong in one direction, the learner is handed a plan built on a person they no longer are. Wrong
/// in the other, the plan reshuffles under their feet for reasons they cannot see — and nothing is
/// more corrosive to a roadmap than watching it change for no reason.
public sealed class RecompileTriggerTests
{
    // ── The plan must move ───────────────────────────────────────────────────

    /// Accepted evidence is the only thing that moves mastery. A concept crossing into HELD leaves
    /// the plan entirely and can unlock a branch that was blocked — a genuinely different plan, and
    /// the learner should see it at once.
    ///
    /// It is also the good news, and good news should not have to wait until 3am.
    [Fact]
    public void Accepted_evidence_recompiles_immediately()
    {
        Assert.True(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.EvidenceAccepted));
    }

    /// A named wrong belief suppresses the concept it sits under, which can push a concept back *out*
    /// of held and into the plan. Continuing to show the old plan there sends someone at a wall.
    [Fact]
    public void A_misconception_recompiles_immediately()
    {
        Assert.True(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.MisconceptionDetected));
    }

    [Fact]
    public void Reality_settling_a_prediction_recompiles_immediately()
    {
        Assert.True(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.PredictionResolved));
    }

    // ── The plan must hold still ─────────────────────────────────────────────

    /// **The thesis, as a recompile rule.** Being taught something does not change what you can do.
    /// The Twin does not even listen to this event — so recompiling would recompute an identical
    /// plan while visibly shuffling the learner's roadmap for a reason they could not perceive.
    [Fact]
    public void Being_taught_something_does_not_recompile_anything()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.ConceptTaught));
    }

    /// An answer moves belief, not mastery credit — and the plan is ordered by credit. A learner who
    /// gets three questions right has finished nothing, and their plan must not rearrange itself as
    /// though they had.
    [Fact]
    public void A_correct_answer_does_not_recompile_the_plan()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.AnswerGiven));
    }

    /// Talking can never finish a concept (the Evidence Ceiling), so it can never change the plan's
    /// shape. Recompiling here would be the machine performing attentiveness.
    [Fact]
    public void Explaining_something_beautifully_does_not_recompile_the_plan()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.ExplanationRecorded));
    }

    /// **The one I had to think hardest about.**
    ///
    /// A learner who walked away mid-session needs the Motivation Engine, not a reshuffled roadmap.
    /// Rewriting their plan the instant they quit would be the product *flinching* — and the next
    /// thing they see should be an unchanged plan and a smaller next step, not evidence that PRAXIS
    /// panicked on their behalf.
    [Fact]
    public void Abandoning_a_session_does_not_rewrite_the_learners_plan()
    {
        Assert.False(
            RecompileTrigger.RequiresImmediateRecompile(LearningEventType.SessionAbandoned),
            "A learner who quit a session came back to a rearranged plan. PRAXIS flinched.");
    }

    /// Failing to prove something is the *default state* — the plan already assumes it. This is
    /// deliberately asymmetric with EvidenceAccepted, and the asymmetry is the point: good news
    /// changes the plan, and the absence of good news does not.
    [Fact]
    public void Failing_to_prove_something_changes_nothing_because_the_plan_already_assumed_it()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.EvidenceRejected));
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.EvidenceAbstained));
    }

    /// Reps land constantly. A rep keeping a concept alive is the plan *working*, not the plan being
    /// wrong — and recompiling on each would mean recompiling all day.
    [Fact]
    public void Reps_do_not_recompile_the_plan()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.RepAnswered));
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.RepDue));
    }

    [Fact]
    public void A_claim_is_not_a_proof()
    {
        // Made a prediction, submitted an artefact — neither is adjudicated yet. Nothing is proved,
        // so nothing has changed.
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.PredictionMade));
        Assert.False(RecompileTrigger.RequiresImmediateRecompile(LearningEventType.ArtifactSubmitted));
    }

    /// **The list is deliberately short.** If most events triggered a recompile, the plan would be in
    /// permanent motion — and a roadmap that reshuffles every time the learner blinks is not a
    /// roadmap, it is an anxiety generator.
    [Fact]
    public void Only_a_handful_of_events_can_move_the_plan()
    {
        var triggering = Enum.GetValues<LearningEventType>()
            .Where(RecompileTrigger.RequiresImmediateRecompile)
            .ToList();

        Assert.Equal(3, triggering.Count);
        Assert.Contains(LearningEventType.EvidenceAccepted, triggering);
        Assert.Contains(LearningEventType.MisconceptionDetected, triggering);
        Assert.Contains(LearningEventType.PredictionResolved, triggering);
    }

    /// A new event type must be an explicit decision, not an accident. Defaulting to "recompile" for
    /// anything unrecognised would mean tomorrow's feature silently starts shuffling everyone's plan.
    [Fact]
    public void An_unrecognised_event_does_not_recompile_by_default()
    {
        Assert.False(RecompileTrigger.RequiresImmediateRecompile((LearningEventType)9999));
    }

    // ── Time is the trigger no event will ever fire ──────────────────────────

    /// **The most important thing that happens to a learner overnight is nothing.**
    ///
    /// Everything they hold decays a little. A concept fresh enough to build on yesterday may not be
    /// today — which changes the cost of everything above it and can put a "finished" concept back
    /// in the plan. No event will ever tell you this, which is why the nightly pass cannot be
    /// replaced by event-driven recompilation however clever the trigger list gets.
    [Fact]
    public void Time_alone_makes_a_plan_stale()
    {
        var compiled = new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc);

        Assert.False(RecompileTrigger.IsStaleByAge(compiled, compiled.AddHours(6)));
        Assert.True(RecompileTrigger.IsStaleByAge(compiled, compiled.AddHours(24)));
    }

    [Fact]
    public void A_learner_with_no_plan_at_all_is_stale_by_definition()
    {
        Assert.True(RecompileTrigger.IsStaleByAge(null, DateTime.UtcNow));
    }
}

public sealed class ObjectiveTests
{
    [Fact]
    public void An_objective_with_no_minutes_behind_it_is_a_wish()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Objective.Set(Guid.CreateVersion7(), Guid.CreateVersion7(), 0));

        Assert.Contains("wish", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_new_objective_has_no_plan_yet()
    {
        var objective = Objective.Set(Guid.CreateVersion7(), Guid.CreateVersion7(), 45);

        Assert.Null(objective.LastCompiledAt);
        Assert.True(RecompileTrigger.IsStaleByAge(objective.LastCompiledAt, DateTime.UtcNow));
    }

    [Fact]
    public void Compiling_records_the_moment_and_the_sequence_it_was_true_at()
    {
        // The sequence is what makes staleness detectable: a plan is a photograph of a learner at a
        // moment, and this says how far the log has moved since.
        var objective = Objective.Set(Guid.CreateVersion7(), Guid.CreateVersion7(), 45);
        var at = new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc);

        objective.RecordCompile(at, sequence: 4_812);

        Assert.Equal(at, objective.LastCompiledAt);
        Assert.Equal(4_812, objective.CompiledAtSequence);
    }

    /// **Renegotiating the pace invalidates the plan**, and it must.
    ///
    /// A learner who drops from 60 minutes to 20 — usually *because* PRAXIS told them the truth about
    /// what their minutes could buy — is a different person to compile for. Serving them the old plan
    /// would be serving them a plan for someone with three times their time.
    [Fact]
    public void Changing_the_pace_throws_the_plan_away()
    {
        var objective = Objective.Set(Guid.CreateVersion7(), Guid.CreateVersion7(), 60);
        objective.RecordCompile(DateTime.UtcNow, 100);

        Assert.NotNull(objective.LastCompiledAt);

        objective.Repace(20);

        Assert.Equal(20, objective.MinutesPerDay);
        Assert.Null(objective.LastCompiledAt);
        Assert.True(RecompileTrigger.IsStaleByAge(objective.LastCompiledAt, DateTime.UtcNow));
    }
}
