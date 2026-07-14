using Praxis.Modules.Graph.Features.Concepts;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Graph.Queries;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

[Collection(SharedGraph.Name)]
public sealed class ConceptSliceTests
{
    private const string Domain = "ai-engineering";

    private readonly GraphFixture _graph;

    public ConceptSliceTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public async Task A_concept_comes_back_with_the_standard_it_will_be_held_to()
    {
        // The learner is entitled to know what would prove it, *before* they are held to it.
        var db = await SeedAsync();

        var result = await new GetConcept.Handler(db)
            .HandleAsync(new GetConcept.Query("beating-the-baseline"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);

        var detail = result.Value;

        Assert.Equal("beating-the-baseline", detail.Concept.Slug);
        Assert.Equal(0.90, detail.Proof.HoldThreshold);

        // And it tells them, in the response, the most a model's opinion could ever be worth.
        Assert.True(detail.Proof.ModelGradedCeiling < detail.Proof.HoldThreshold);

        // The build target cannot be talked into existence.
        Assert.Contains(detail.Proof.Accepted, a => a.Type == EvidenceType.Build && !a.ModelGraded);
    }

    [Fact]
    public async Task A_concept_reports_its_prerequisites_and_what_it_is_confused_with()
    {
        var db = await SeedAsync();

        var result = await new GetConcept.Handler(db)
            .HandleAsync(new GetConcept.Query("embeddings"), CancellationToken.None);

        Assert.Contains("tokenisation", result.Value.Requires, StringComparer.Ordinal);

        // Embeddings CONTRASTS bm25 — the pair people reliably conflate, and where the Assessment
        // engine will go hunting for its best distractors.
        Assert.Contains("bm25", result.Value.Contrasts, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_contrast_is_visible_from_both_ends()
    {
        // The pack authored it in one direction, but the learner confuses them with *each other*.
        var db = await SeedAsync();

        var bm25 = await new GetConcept.Handler(db)
            .HandleAsync(new GetConcept.Query("bm25"), CancellationToken.None);

        Assert.Contains("embeddings", bm25.Value.Contrasts, StringComparer.Ordinal);
    }

    [Fact]
    public async Task An_unknown_concept_is_not_found()
    {
        var db = await SeedAsync();

        var result = await new GetConcept.Handler(db)
            .HandleAsync(new GetConcept.Query("no-such-thing"), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Listing_returns_the_domain_easiest_first()
    {
        var db = await SeedAsync();

        var result = await new ListConcepts.Handler(db)
            .HandleAsync(new ListConcepts.Query(Domain, PageSize: 200), CancellationToken.None);

        Assert.Equal(30, result.Value.TotalCount);

        var difficulties = result.Value.Items.Select(i => i.DifficultyPrior).ToList();
        Assert.Equal(difficulties.OrderBy(d => d), difficulties);

        // Tokenisation is the gentlest thing in the pack, so it leads.
        Assert.Equal("tokenisation", result.Value.Items[0].Slug);
    }

    [Fact]
    public async Task Listing_can_be_searched()
    {
        var db = await SeedAsync();

        var result = await new ListConcepts.Handler(db)
            .HandleAsync(new ListConcepts.Query(Domain, Search: "eval"), CancellationToken.None);

        Assert.Contains(result.Value.Items, c => c.Slug == "the-eval-trap");
        Assert.DoesNotContain(result.Value.Items, c => c.Slug == "tokenisation");
    }

    [Fact]
    public async Task The_frontier_of_a_learner_who_knows_nothing_is_one_concept()
    {
        var db = await SeedAsync();

        var result = await new GetFrontier.Handler(new GraphQueries(db))
            .HandleAsync(new GetFrontier.Query(Domain, []), CancellationToken.None);

        Assert.Single(result.Value.Ready);
        Assert.Equal("tokenisation", result.Value.Ready[0].Slug);

        // And everything else is reported with a distance, not a locked door.
        Assert.NotEmpty(result.Value.Blocked);
        Assert.All(result.Value.Blocked, b => Assert.True(b.MissingPrerequisites > 0));
    }

    [Fact]
    public async Task The_frontier_moves_when_the_learner_holds_something()
    {
        var db = await SeedAsync();

        var tokenisation = db.Concepts.Single(c => c.Slug == "tokenisation");

        var result = await new GetFrontier.Handler(new GraphQueries(db))
            .HandleAsync(new GetFrontier.Query(Domain, [tokenisation.Id]), CancellationToken.None);

        var ready = result.Value.Ready.Select(c => c.Slug).ToList();

        Assert.Contains("the-inverted-index", ready, StringComparer.Ordinal);
        Assert.Contains("embeddings", ready, StringComparer.Ordinal);
        Assert.DoesNotContain("tokenisation", ready, StringComparer.Ordinal);
    }

    /// The one M2.6 names explicitly. A cyclic graph must never reach a learner — and the refusal
    /// happens at the loader, so the API layer never has to cope with a graph that cannot be
    /// walked.
    [Fact]
    public async Task A_cyclic_pack_is_refused_and_the_graph_is_untouched()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = $"cyc{Guid.CreateVersion7():N}"[..12];
        var before = db.Concepts.Count();

        var result = await loader.LoadAsync($$"""
            domain: {{domain}}
            version: 1
            note: A cycle that must never reach a learner.
            concepts:
              - slug: {{domain}}-a
                title: A
                summary: Requires B.
                difficulty: 0
                minutes: 30
                requires: [{{domain}}-b]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: {{domain}}-b
                title: B
                summary: Requires A. Which requires B. Which requires A.
                difficulty: 0
                minutes: 30
                requires: [{{domain}}-a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, e => e.Contains("REQUIRES cycle", StringComparison.Ordinal));

        // Nothing was written. A learner can never be handed a graph that cannot be walked.
        Assert.Equal(before, db.Concepts.Count());
    }

    [Fact]
    public async Task The_whole_graph_comes_back_in_one_read_as_nodes_and_prerequisite_edges()
    {
        var db = await SeedAsync();

        var result = await new GetGraphStructure.Handler(db)
            .HandleAsync(new GetGraphStructure.Query(Domain), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        var graph = result.Value;

        // Every concept is a node; the graph is dense enough to be a real dependency map.
        Assert.Equal(30, graph.Nodes.Count);
        Assert.True(graph.Edges.Count >= 30, $"Only {graph.Edges.Count} edges — too sparse to be real.");

        // Every edge connects two nodes that exist — no arrow points into the void.
        var ids = graph.Nodes.Select(n => n.ConceptId).ToHashSet();
        Assert.All(graph.Edges, e =>
        {
            Assert.Contains(e.From, ids);
            Assert.Contains(e.To, ids);
        });

        // The map has one true source and one true sink, exactly as the pack promises: nothing is a
        // prerequisite of `tokenisation` (no edge points *to* it), and `beating-the-baseline` unlocks
        // nothing (no edge points *from* it).
        var tokenisation = graph.Nodes.Single(n => n.Slug == "tokenisation").ConceptId;
        var objective = graph.Nodes.Single(n => n.Slug == "beating-the-baseline").ConceptId;
        Assert.DoesNotContain(graph.Edges, e => e.To == tokenisation);
        Assert.DoesNotContain(graph.Edges, e => e.From == objective);
    }

    [Fact]
    public async Task A_domain_with_no_graph_is_a_not_found_not_an_empty_map()
    {
        var db = await SeedAsync();

        var result = await new GetGraphStructure.Handler(db)
            .HandleAsync(new GetGraphStructure.Query("no-such-domain"), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    private async Task<GraphDbContext> SeedAsync()
    {
        var db = _graph.CreateContext();

        if (db.Concepts.Any(c => c.Domain == Domain))
        {
            return db;   // the shared container already has it
        }

        var result = await new PackLoader(db, new SystemClock()).LoadAsync(SeedPack());
        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));

        return db;
    }

    private static string SeedPack()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "ai-engineering", "pack.yaml"));
    }
}
