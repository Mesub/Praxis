using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// A wrong graph does not throw. It quietly wastes a learner's month (R-2). So the pack has to be
/// refused *before* it reaches anyone, and the refusal has to say what is wrong — all of it, at
/// once, because an author fixing 180 concepts one error per run stops caring about the graph.
public sealed class PackValidatorTests
{
    [Fact]
    public void An_honest_pack_passes()
    {
        var result = PackValidator.Validate(Parse(Honest));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        Assert.Equal(3, result.Value.BySlug.Count);
    }

    [Fact]
    public void A_cycle_in_requires_is_refused_and_the_cycle_is_named()
    {
        // A needs B, B needs C, C needs A. The Compiler would either never terminate, or silently
        // drop an edge to make progress. The first hangs; the second lies.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A cycle.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                requires: [c]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: c
                title: C
                summary: C.
                difficulty: 0
                minutes: 30
                requires: [b]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("REQUIRES cycle", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("->", StringComparison.Ordinal));
    }

    [Fact]
    public void A_concept_that_requires_itself_is_refused()
    {
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: Self-reference.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("on itself", StringComparison.Ordinal));
    }

    [Fact]
    public void A_concept_with_no_proof_contract_is_refused()
    {
        // Nothing could ever demonstrate it. The learner would work on it forever and never hold
        // it — which is precisely the experience PRAXIS exists to abolish.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: No proof.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("topic, not a concept", StringComparison.Ordinal));
    }

    [Fact]
    public void A_contract_that_breaches_the_evidence_ceiling_is_refused_by_name()
    {
        // The author is being generous, and the generosity would let a language model's opinion
        // alone confer mastery (C-4). The error names the concept, so they know which one.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A dishonest contract.
            concepts:
              - slug: honest
                title: Honest
                summary: Fine.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: dishonest
                title: Dishonest
                summary: Talking is enough here, apparently.
                difficulty: 0
                minutes: 30
                requires: [honest]
                proof:
                  hold: 0.8
                  evidence:
                    - { type: Explain, min-score: 0.7, max: 0.5 }
                    - { type: Critique, min-score: 0.7, max: 0.4 }
                    - { type: Apply, min-score: 0.8, max: 0.85 }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e =>
            e.Contains("'dishonest'", StringComparison.Ordinal)
            && e.Contains("hallucinating grader", StringComparison.Ordinal));
    }

    [Fact]
    public void An_orphan_is_refused()
    {
        // Either the author forgot its prerequisites, and a learner gets walked into a wall — or
        // it does not belong in this pack.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: An orphan.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: lonely
                title: Lonely
                summary: Connected to nothing.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("'lonely' is connected to nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_prerequisite_pointing_at_nothing_is_refused()
    {
        // A typo in a prerequisite is indistinguishable from a missing prerequisite until someone
        // fails the concept it was supposed to unlock.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A typo.
            concepts:
              - slug: embeddings
                title: Embeddings
                summary: Vectors.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: retrieval
                title: Retrieval
                summary: Search.
                difficulty: 0
                minutes: 30
                requires: [embedings]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("'embedings', which does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_slug_is_refused()
    {
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: Duplicates.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: a
                title: A again
                summary: The same slug.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("defined twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_problem_is_reported_at_once_not_just_the_first()
    {
        // The whole point. An author fixing a 180-concept graph one error per run gives up on the
        // graph, and a graph nobody maintains is worse than no graph.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: Several problems at once.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                requires: [nonexistent]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: No proof at all.
                difficulty: 0
                minutes: 30
                requires: [a]
              - slug: orphan
                title: Orphan
                summary: Alone.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.True(result.Errors.Count >= 3, $"Only reported: {string.Join(" | ", result.Errors)}");

        Assert.Contains(result.Errors, e => e.Contains("does not exist", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("topic, not a concept", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("connected to nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_misconception_with_no_key_is_refused()
    {
        // A misconception the extractor cannot name is one it cannot match across learners, so it
        // can never be mined — which is the entire reason keys exist.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A keyless misconception.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { statement: I believe a wrong thing. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("misconception with no key", StringComparison.Ordinal));
    }

    [Fact]
    public void A_misconception_key_that_is_not_kebab_case_is_refused()
    {
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A prose key.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { key: "More Context Is Better", statement: I believe more context is better. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("is not kebab-case", StringComparison.Ordinal));
    }

    [Fact]
    public void A_misconception_key_longer_than_64_characters_is_refused()
    {
        // Assessment's learner-model columns (BankedItem.TargetsMisconception,
        // LearnerMisconception.Key) are HasMaxLength(64). A key the pack loader accepts but the
        // learner model cannot store is a key that survives the airlock and then blows up
        // SaveChangesAsync in EnsureConceptPool — losing the whole generated batch, silently.
        var longKey = new string('a', 65);

        var result = PackValidator.Validate(Parse($$"""
            domain: test
            version: 1
            note: A key too long for the learner-model columns.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { key: {{longKey}}, statement: I believe a wrong thing. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("cannot exceed 64 characters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_misconception_key_of_exactly_64_characters_passes()
    {
        // The boundary: 64 is the limit, not the first refused length.
        var boundaryKey = new string('a', 64);

        var result = PackValidator.Validate(Parse($$"""
            domain: test
            version: 1
            note: A key exactly at the learner-model column limit.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { key: {{boundaryKey}}, statement: I believe a wrong thing. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void A_misconception_with_no_statement_is_refused()
    {
        // A misconception a learner cannot read back in their own voice is one they will never
        // recognise as theirs — and one they do not recognise is one they keep.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A statementless misconception.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { key: some-wrong-idea }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("has no statement", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_misconception_key_on_two_concepts_is_refused()
    {
        // A key names one wrong idea across the whole pack. Declaring it twice would either split one
        // belief in two or claim two beliefs are one — and the cohort-scale mining quietly breaks.
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: A key declared twice.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                misconceptions:
                  - { key: shared-wrong-idea, statement: I believe it here. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                misconceptions:
                  - { key: shared-wrong-idea, statement: And I believe it here too. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e =>
            e.Contains("'shared-wrong-idea' is declared on both", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pack_with_well_formed_misconceptions_passes()
    {
        var result = PackValidator.Validate(Parse("""
            domain: test
            version: 1
            note: Honest, with a named wrong belief.
            concepts:
              - slug: a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [a]
                contrasts: [a]
                misconceptions:
                  - { key: b-is-just-a, statement: I think B is really just A with a new name. }
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Broken_yaml_is_reported_with_a_line_number()
    {
        var result = PackLoader.Parse("domain: test\nconcepts:\n  - slug: a\n   bad indent here");

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("line", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_format_may_only_be_declared_on_apply_evidence()
    {
        // The Apply clause alone already clears the Evidence Ceiling (max 0.85 >= hold 0.80), so
        // PoMContract.Create raises nothing on its own. If this pack is Invalid at all, it can only
        // be because of the format rule under test — not because of the unrelated "needs at least
        // one of APPLY, BUILD or PREDICT" floor check, whose message also happens to contain the
        // substring "APPLY" and would otherwise make this test pass for the wrong reason.
        var doc = OneConcept(evidence:
        [
            new PackEvidence { Type = EvidenceType.Apply, MinScore = 0.8, Max = 0.85, Count = 1 },
            new PackEvidence
            {
                Type = EvidenceType.Explain,
                MinScore = 0.7,
                Max = 0.20,
                Count = 1,
                Format = ItemFormat.MultipleChoice,
            },
        ]);

        var result = PackValidator.Validate(doc);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e =>
            e.Contains("is marked against a fixed key", StringComparison.Ordinal));
    }

    [Fact]
    public void Multiple_choice_without_a_declared_misconception_is_refused()
    {
        // Distractors exist to catch a named wrong belief. With none declared there is nothing to
        // build them out of, and three of the four options would be noise.
        var doc = OneConcept(
            evidence:
            [
                new PackEvidence
                {
                    Type = EvidenceType.Apply,
                    MinScore = 0.7,
                    Max = 0.85,
                    Count = 1,
                    Format = ItemFormat.MultipleChoice,
                },
            ],
            misconceptions: []);

        var result = PackValidator.Validate(doc);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("misconception", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Multiple_choice_apply_with_a_declared_misconception_is_accepted()
    {
        var doc = OneConcept(
            evidence:
            [
                new PackEvidence
                {
                    Type = EvidenceType.Apply,
                    MinScore = 0.7,
                    Max = 0.85,
                    Count = 1,
                    Format = ItemFormat.MultipleChoice,
                },
            ],
            misconceptions: [new PackMisconception { Key = "keyword-match-wins", Statement = "I think the option that repeats the text is right." }]);

        Assert.True(PackValidator.Validate(doc).IsSuccess);
    }

    private static PackDocument OneConcept(
        IReadOnlyList<PackEvidence> evidence,
        List<PackMisconception>? misconceptions = null)
        => new()
        {
            Domain = "test-domain",
            Version = 1,
            Note = "a pack for one test",
            Concepts =
            [
                new PackConcept
                {
                    Slug = "only-concept",
                    Title = "Only concept",
                    Summary = "The only concept in this pack.",
                    Difficulty = 0.0,
                    Minutes = 30,
                    Misconceptions = misconceptions ??
                        [new PackMisconception { Key = "some-belief", Statement = "I think a wrong thing." }],
                    Proof = new PackContract { Hold = 0.80, Evidence = [.. evidence] },
                },
            ],
        };

    private static PackDocument Parse(string yaml)
    {
        var parsed = PackLoader.Parse(yaml);
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }

    private const string Honest = """
        domain: test
        version: 1
        note: Three honest concepts.
        concepts:
          - slug: embeddings
            title: Embeddings
            summary: Dense vector representations of text.
            difficulty: -0.2
            minutes: 60
            proof:
              hold: 0.80
              evidence:
                - { type: Explain, min-score: 0.7, max: 0.25 }
                - { type: Apply, min-score: 0.8, max: 0.80, count: 2 }
          - slug: vector-retrieval
            title: Vector retrieval
            summary: Nearest-neighbour search over embeddings.
            difficulty: 0.6
            minutes: 90
            requires: [embeddings]
            proof:
              hold: 0.80
              evidence:
                - { type: Build, min-score: 1.0, max: 0.85 }
                - { type: Explain, min-score: 0.7, max: 0.20 }
          - slug: bm25
            title: BM25
            summary: The lexical baseline a RAG system must actually beat.
            difficulty: 0.3
            minutes: 45
            contrasts: [vector-retrieval]
            proof:
              hold: 0.80
              evidence:
                - { type: Apply, min-score: 0.8, max: 0.80 }
                - { type: Recall, min-score: 0.9, max: 0.15 }
        """;
}
