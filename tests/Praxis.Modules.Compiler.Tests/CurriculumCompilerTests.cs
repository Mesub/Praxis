using Praxis.Modules.Compiler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Tests;

/// **The plan is compiled, not authored.**
///
/// A graph shaped like the real pack's spine:
///
///     tokenisation
///        ├── the-inverted-index ── bm25 ──┐
///        └── embeddings ──┬── vector-index ┤
///                         └── chunking ────┴── retrieval ── beating-the-baseline
///
public sealed class CurriculumCompilerTests
{
    [Fact]
    public void A_learner_who_knows_nothing_gets_the_whole_path()
    {
        var plan = Compile(Graph());

        Assert.Equal(8, plan.Steps.Count);
        Assert.Equal(0, plan.ConceptsAlreadyHeld);
        Assert.All(plan.Steps, s => Assert.Equal(StepKind.Learn, s.Kind));
    }

    [Fact]
    public void No_step_ever_arrives_before_something_it_requires()
    {
        // The one property that cannot be wrong. If it is, PRAXIS sends a learner at a concept they
        // are not equipped for, and they blame themselves.
        var plan = Compile(Graph());
        var position = plan.Steps.ToDictionary(s => s.Slug, s => s.Position);

        foreach (var (dependent, prerequisite) in Dependencies())
        {
            Assert.True(
                position[prerequisite] < position[dependent],
                $"'{dependent}' was scheduled before '{prerequisite}', which it requires.");
        }
    }

    /// **The test that nearly was not there.**
    ///
    /// The first version of this suite checked ordering on a graph where every prerequisite happened
    /// to be *cheaper* than its dependent — so a greedy cheapest-first walk produced the right order
    /// even with the prerequisite gate deleted. The ordering guarantee was resting on a coincidence
    /// of the cost function, and the test proved nothing.
    ///
    /// So: a 5-minute concept sitting on top of a 300-minute one. Cheapest-first *wants* to schedule
    /// the cheap one immediately. It must not be allowed to, however tempting the number — because a
    /// plan that hands someone a concept they are not equipped for is worse than no plan.
    [Fact]
    public void A_cheap_concept_never_jumps_ahead_of_its_expensive_prerequisite()
    {
        var hard = Guid.CreateVersion7();
        var easy = Guid.CreateVersion7();

        var graph = new List<CompilerConcept>
        {
            Concept(hard, "a-brutal-foundation", []) with { EstimatedMinutes = 300 },
            Concept(easy, "z-trivial-consequence", [hard]) with { EstimatedMinutes = 5 },
        };

        var result = CurriculumCompiler.Compile(new CompileRequest(easy, graph, 45));

        Assert.True(result.IsSuccess, result.Message);

        var plan = result.Value;

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(
            "a-brutal-foundation", plan.Steps[0].Slug);

        Assert.Equal(
            "z-trivial-consequence", plan.Steps[1].Slug);

        // The slug ordering is deliberately against us too: alphabetically 'a' < 'z', so if the
        // ordering ever fell back to the tie-break it would still pass. It is the prerequisite gate
        // that must do this, so make the cost overwhelmingly favour the wrong answer and check.
        Assert.True(plan.Steps[0].Minutes > plan.Steps[1].Minutes * 5);
    }

    [Fact]
    public void The_objective_comes_last_because_everything_serves_it()
    {
        var plan = Compile(Graph());

        Assert.Equal("beating-the-baseline", plan.Steps[^1].Slug);
    }

    [Fact]
    public void The_plan_starts_at_the_only_place_it_can()
    {
        var plan = Compile(Graph());

        Assert.Equal("tokenisation", plan.Steps[0].Slug);
    }

    // ── The cache ────────────────────────────────────────────────────────────

    [Fact]
    public void What_the_learner_already_holds_is_never_taught_again()
    {
        // The cache hit. Time is the learner's scarcest resource, and re-teaching them something
        // they can demonstrate is theft.
        var graph = Graph(held: ["tokenisation", "embeddings"], retrievability: 0.95);

        var plan = Compile(graph);

        Assert.Equal(2, plan.ConceptsAlreadyHeld);
        Assert.DoesNotContain(plan.Steps, s => s.Slug == "tokenisation");
        Assert.DoesNotContain(plan.Steps, s => s.Slug == "embeddings");
        Assert.Equal(6, plan.Steps.Count);
    }

    [Fact]
    public void Holding_more_makes_the_plan_shorter()
    {
        var fromNothing = Compile(Graph());
        var withFoundations = Compile(Graph(held: ["tokenisation", "embeddings"], retrievability: 0.95));

        Assert.True(withFoundations.TotalMinutes < fromNothing.TotalMinutes);
        Assert.True(withFoundations.DaysAtStatedPace < fromNothing.DaysAtStatedPace);
    }

    // ── Cache invalidation. This is the part that makes it a compiler ────────

    /// **A concept they "hold" and have let rot goes back in the plan.**
    ///
    /// Every other roadmap tool would tick it off and move on. PRAXIS puts it back — because if it
    /// does not, the learner arrives at the concept that depends on it and finds it *impossible*,
    /// and blames themselves.
    [Fact]
    public void A_foundation_that_has_rotted_comes_back_as_a_refresh()
    {
        var graph = Graph(held: ["tokenisation", "embeddings"], retrievability: 0.35);

        var plan = Compile(graph);

        Assert.Equal(0, plan.ConceptsAlreadyHeld);
        Assert.Equal(2, plan.ConceptsNeedingRefresh);

        var refreshes = plan.Steps.Where(s => s.Kind == StepKind.Refresh).Select(s => s.Slug).ToList();

        Assert.Contains("tokenisation", refreshes, StringComparer.Ordinal);
        Assert.Contains("embeddings", refreshes, StringComparer.Ordinal);
    }

    [Fact]
    public void A_refresh_is_cheaper_than_learning_it_from_scratch_but_it_is_not_free()
    {
        var never = Compile(Graph());
        var rotted = Compile(Graph(held: ["tokenisation"], retrievability: 0.30, masteryCredit: 0.85));

        var learnCost = never.Steps.Single(s => s.Slug == "tokenisation").Minutes;
        var refreshCost = rotted.Steps.Single(s => s.Slug == "tokenisation").Minutes;

        Assert.True(refreshCost < learnCost, "Relearning cost as much as learning. It does not.");
        Assert.True(refreshCost > 0, "Relearning was free. It is not — and pretending so is how a "
            + "learner is told they are ready when they are not.");
    }

    [Fact]
    public void A_barely_fresh_concept_is_kept_and_a_barely_stale_one_is_not()
    {
        var fresh = Compile(Graph(held: ["tokenisation"], retrievability: CurriculumCompiler.FreshEnough + 0.01));
        var stale = Compile(Graph(held: ["tokenisation"], retrievability: CurriculumCompiler.FreshEnough - 0.01));

        Assert.Equal(1, fresh.ConceptsAlreadyHeld);
        Assert.Equal(0, stale.ConceptsAlreadyHeld);
        Assert.Equal(1, stale.ConceptsNeedingRefresh);
    }

    // ── The ordering keeps foundations warm ──────────────────────────────────

    /// The reason the order is not arbitrary. A concept whose prerequisites the learner has *just
    /// done* is cheaper than one whose prerequisites are cold — so the compiler naturally clusters
    /// dependent work together rather than scattering it.
    [Fact]
    public void Work_scheduled_in_this_plan_is_treated_as_fresh_when_its_dependents_arrive()
    {
        var plan = Compile(Graph());

        var embeddings = plan.Steps.Single(s => s.Slug == "embeddings").Position;
        var vectorIndex = plan.Steps.Single(s => s.Slug == "vector-index").Position;

        Assert.True(embeddings < vectorIndex);

        // And the dependent is not penalised for a rotting prerequisite, because it has not rotted:
        // the learner did it days ago, inside this plan.
        var cold = Compile(Graph(held: ["tokenisation", "embeddings"], retrievability: 0.30));

        var coldVectorIndex = cold.Steps.Single(s => s.Slug == "vector-index").Minutes;
        var warmVectorIndex = plan.Steps.Single(s => s.Slug == "vector-index").Minutes;

        Assert.True(
            coldVectorIndex > warmVectorIndex,
            "Building on a rotted prerequisite cost no more than building on a fresh one.");
    }

    // ── The honest total ─────────────────────────────────────────────────────

    [Fact]
    public void Cumulative_minutes_accumulate_and_days_follow_the_learners_actual_pace()
    {
        var plan = Compile(Graph(), minutesPerDay: 30);

        var running = 0.0;

        foreach (var step in plan.Steps)
        {
            running += step.Minutes;
            Assert.Equal(running, step.CumulativeMinutes, precision: 6);
        }

        Assert.Equal(running, plan.TotalMinutes, precision: 6);
        Assert.Equal(running / 30, plan.DaysAtStatedPace, precision: 6);

        // And the day a learner reaches a step is honest about how little time they have.
        Assert.Equal((int)Math.Ceiling(plan.Steps[^1].CumulativeMinutes / 30), plan.Steps[^1].DayReached);
    }

    [Fact]
    public void Fewer_minutes_a_day_means_more_days_not_less_work()
    {
        var patient = Compile(Graph(), minutesPerDay: 15);
        var committed = Compile(Graph(), minutesPerDay: 90);

        Assert.Equal(patient.TotalMinutes, committed.TotalMinutes, precision: 6);
        Assert.True(patient.DaysAtStatedPace > committed.DaysAtStatedPace);
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_objective_that_is_not_in_the_graph_is_refused()
    {
        var result = CurriculumCompiler.Compile(
            new CompileRequest(Guid.CreateVersion7(), Graph(), 45));

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public void A_cycle_is_refused_rather_than_hung_on()
    {
        // The pack validator refuses to publish a cycle, so reaching here means the graph was
        // written outside the gate. Say so — do not loop forever, and do not silently drop an edge.
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var cyclic = new List<CompilerConcept>
        {
            Concept(a, "a", [b]),
            Concept(b, "b", [a]),
        };

        var result = CurriculumCompiler.Compile(new CompileRequest(a, cyclic, 45));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("cycle", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zero_minutes_a_day_is_refused()
    {
        var result = CurriculumCompiler.Compile(new CompileRequest(Objective, Graph(), 0));

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public void A_learner_who_already_holds_everything_gets_an_empty_plan()
    {
        var graph = Graph(
            held: [.. Slugs],
            retrievability: 0.95);

        var plan = Compile(graph);

        Assert.True(plan.IsEmpty);
        Assert.Equal(8, plan.ConceptsAlreadyHeld);
        Assert.Equal(0.0, plan.TotalMinutes);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    /// **Compile the same learner twice and get the same plan.**
    ///
    /// The whole reason there is no language model in this path. A roadmap that changes when you ask
    /// again is not a roadmap; it is a mood. A learner cannot trust — or commit to — a plan that
    /// will be different tomorrow for no reason.
    [Fact]
    public void The_same_learner_compiles_to_the_same_plan_every_time()
    {
        var first = Compile(Graph());
        var second = Compile(Graph());

        Assert.Equal(
            first.Steps.Select(s => (s.Slug, s.Position, s.Minutes)),
            second.Steps.Select(s => (s.Slug, s.Position, s.Minutes)));

        Assert.Equal(first.TotalMinutes, second.TotalMinutes, precision: 12);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static readonly Guid Objective = new("00000000-0000-0000-0000-000000000008");

    private static readonly string[] Slugs =
    [
        "tokenisation", "the-inverted-index", "bm25", "embeddings",
        "vector-index", "chunking", "retrieval", "beating-the-baseline",
    ];

    private static IEnumerable<(string Dependent, string Prerequisite)> Dependencies()
    {
        yield return ("the-inverted-index", "tokenisation");
        yield return ("embeddings", "tokenisation");
        yield return ("bm25", "the-inverted-index");
        yield return ("vector-index", "embeddings");
        yield return ("chunking", "embeddings");
        yield return ("retrieval", "vector-index");
        yield return ("retrieval", "chunking");
        yield return ("beating-the-baseline", "retrieval");
        yield return ("beating-the-baseline", "bm25");
    }

    private static Guid Id(string slug) => new($"00000000-0000-0000-0000-{Array.IndexOf(Slugs, slug) + 1:D12}");

    private static Plan Compile(IReadOnlyList<CompilerConcept> graph, int minutesPerDay = 45)
    {
        var result = CurriculumCompiler.Compile(new CompileRequest(Objective, graph, minutesPerDay));

        Assert.True(result.IsSuccess, result.Message);

        return result.Value;
    }

    private static List<CompilerConcept> Graph(
        string[]? held = null,
        double retrievability = 1.0,
        double masteryCredit = 0.90)
    {
        var isHeld = (held ?? []).ToHashSet(StringComparer.Ordinal);

        var requires = Dependencies()
            .GroupBy(d => d.Dependent)
            .ToDictionary(g => g.Key, g => g.Select(d => Id(d.Prerequisite)).ToList());

        return [.. Slugs.Select(slug => new CompilerConcept(
            Id: Id(slug),
            Slug: slug,
            Requires: requires.GetValueOrDefault(slug, []),
            EstimatedMinutes: 60,
            Contract: Typical,
            MasteryCredit: isHeld.Contains(slug) ? masteryCredit : 0.0,
            IsHeld: isHeld.Contains(slug),
            Retrievability: isHeld.Contains(slug) ? retrievability : 0.0,
            LearnerDifficulty: 5.0))];
    }

    private static CompilerConcept Concept(Guid id, string slug, IReadOnlyList<Guid> requires)
        => new(id, slug, requires, 60, Typical, 0.0, false, 0.0, 5.0);

    private static readonly MasteryContract Typical = new(
        HoldThreshold: 0.80,
        Clauses:
        [
            new EvidenceClause(EvidenceType.Explain, 0.7, 0.25, 1),
            new EvidenceClause(EvidenceType.Apply, 0.8, 0.60, 2),
            new EvidenceClause(EvidenceType.Build, 1.0, 0.85, 1),
        ]);
}
