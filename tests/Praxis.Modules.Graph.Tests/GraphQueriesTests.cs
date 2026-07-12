using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Graph.Queries;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// Traversal, on a graph shaped like a real one.
///
///     tokenisation
///          |
///      embeddings ──────────────┐
///          |                    |
///     vector-index         chunk-sizing
///          |                    |
///          |                windowing        &lt;- the deep branch is one hop longer
///          |                    |
///          └───── retrieval ────┘
///                     |
///                  reranking
///
/// The diamond is **asymmetric on purpose**. `retrieval` is three hops away down one branch and
/// four down the other, and it cannot be started until the *slower* chain is finished. A traversal
/// that took the shortest path would schedule it a concept too early — and `windowing` sorts after
/// `retrieval` alphabetically, so that mistake cannot hide behind a lucky tie-break.
///
/// bm25 stands apart: it requires only tokenisation, and CONTRASTS with retrieval — a relationship
/// that must NOT order anything.
[Collection(SharedGraph.Name)]
public sealed class GraphQueriesTests
{
    private readonly GraphFixture _graph;

    public GraphQueriesTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public async Task Prerequisites_are_transitive()
    {
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var prereqs = await queries.PrerequisitesOfAsync(ids["retrieval"]);

        // Everything upstream, not just the immediate parents.
        Assert.Equal(
            ["chunk-sizing", "embeddings", "tokenisation", "vector-index", "windowing"],
            prereqs.Select(c => Bare(c.Slug, domain)).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_root_concept_requires_nothing()
    {
        var (db, _, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        Assert.Empty(await queries.PrerequisitesOfAsync(ids["tokenisation"]));
    }

    [Fact]
    public async Task Dependents_answer_what_skipping_this_would_cost()
    {
        // "What breaks if I skip embeddings?" — the counterfactual a learner actually wants.
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var dependents = await queries.DependentsOfAsync(ids["embeddings"]);

        Assert.Equal(
            ["chunk-sizing", "reranking", "retrieval", "vector-index", "windowing"],
            dependents.Select(c => Bare(c.Slug, domain)).OrderBy(s => s, StringComparer.Ordinal));

        // bm25 is not downstream of embeddings. It CONTRASTS with retrieval, and a contrast
        // orders nothing.
        Assert.DoesNotContain("bm25", dependents.Select(c => Bare(c.Slug, domain)), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_contrast_edge_creates_no_dependency()
    {
        // The load-bearing claim of the whole edge model. If CONTRASTS ever ordered anything, the
        // Compiler would invent prerequisites nobody authored.
        var (db, _, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var prereqs = await queries.PrerequisitesOfAsync(ids["bm25"]);

        Assert.Single(prereqs);
        Assert.Equal(ids["tokenisation"], prereqs[0].Id);
    }

    [Fact]
    public async Task Topological_order_never_puts_a_concept_before_something_it_requires()
    {
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var ordered = await queries.TopologicalOrderAsync(domain);
        var position = ordered.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);

        Assert.Equal(8, ordered.Count);

        // Every REQUIRES edge must point forwards in this list.
        foreach (var (dependent, prerequisite) in Dependencies())
        {
            Assert.True(
                position[ids[prerequisite]] < position[ids[dependent]],
                $"'{prerequisite}' must come before '{dependent}', but it does not.");
        }
    }

    [Fact]
    public async Task An_asymmetric_diamond_takes_its_deepest_path()
    {
        // retrieval is three hops down one branch and four down the other. It cannot be started
        // until the SLOWER chain is done. A shortest-path walk places it a concept too early, and
        // the learner is sent at something they are not equipped for.
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var ordered = await queries.TopologicalOrderAsync(domain);
        var position = ordered.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);

        Assert.True(position[ids["retrieval"]] > position[ids["vector-index"]]);

        Assert.True(
            position[ids["retrieval"]] > position[ids["windowing"]],
            "retrieval was scheduled before windowing, which it requires. The traversal took the "
            + "shortest path into the diamond rather than the longest.");

        Assert.True(position[ids["reranking"]] > position[ids["retrieval"]]);
    }

    [Fact]
    public async Task A_learner_who_knows_nothing_can_only_start_at_the_roots()
    {
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var frontier = await queries.FrontierAsync(domain, []);

        Assert.Single(frontier);
        Assert.Equal(ids["tokenisation"], frontier[0].Id);
    }

    [Fact]
    public async Task The_frontier_advances_as_the_learner_holds_more()
    {
        // The one dopamine PRAXIS permits itself: the frontier moving. It is earned and it is real.
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var afterTokenisation = await queries.FrontierAsync(domain, [ids["tokenisation"]]);

        Assert.Equal(
            ["bm25", "embeddings"],
            afterTokenisation.Select(c => Bare(c.Slug, domain)).OrderBy(s => s, StringComparer.Ordinal));

        var afterEmbeddings = await queries.FrontierAsync(
            domain, [ids["tokenisation"], ids["embeddings"]]);

        Assert.Equal(
            ["bm25", "chunk-sizing", "vector-index"],
            afterEmbeddings.Select(c => Bare(c.Slug, domain)).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_concept_with_one_prerequisite_still_missing_is_not_on_the_frontier()
    {
        // The diamond again: holding vector-index is not enough for retrieval, because the other
        // branch is unfinished. Getting this wrong walks the learner into a wall.
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var frontier = await queries.FrontierAsync(
            domain, [ids["tokenisation"], ids["embeddings"], ids["vector-index"]]);

        var slugs = frontier.Select(c => Bare(c.Slug, domain)).ToList();

        Assert.DoesNotContain("retrieval", slugs, StringComparer.Ordinal);
        Assert.Contains("chunk-sizing", slugs, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_concept_already_held_is_not_on_the_frontier()
    {
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var frontier = await queries.FrontierAsync(domain, [ids["tokenisation"]]);

        Assert.DoesNotContain(ids["tokenisation"], frontier.Select(c => c.Id));
    }

    [Fact]
    public async Task Blocked_concepts_report_how_far_away_they_are()
    {
        // The difference between a wall and a ladder: "you are not ready" versus "you are two
        // concepts away".
        var (db, domain, ids) = await SeedAsync();
        var queries = new GraphQueries(db);

        var blocked = await queries.BlockedAsync(domain, [ids["tokenisation"], ids["embeddings"]]);

        var retrieval = blocked.Single(b => Bare(b.Slug, domain) == "retrieval");

        // Both vector-index and windowing are still missing.
        Assert.Equal(2, retrieval.MissingPrerequisites);

        // And nothing on the frontier appears here.
        Assert.DoesNotContain(blocked, b => Bare(b.Slug, domain) == "chunk-sizing");
    }

    private static IEnumerable<(string Dependent, string Prerequisite)> Dependencies()
    {
        yield return ("embeddings", "tokenisation");
        yield return ("bm25", "tokenisation");
        yield return ("vector-index", "embeddings");
        yield return ("chunk-sizing", "embeddings");
        yield return ("windowing", "chunk-sizing");
        yield return ("retrieval", "vector-index");
        yield return ("retrieval", "windowing");
        yield return ("reranking", "retrieval");
    }

    private static string Bare(string slug, string domain)
        => slug.Replace($"-{domain}", "", StringComparison.Ordinal);

    private async Task<(GraphDbContext Db, string Domain, Dictionary<string, Guid> Ids)> SeedAsync()
    {
        var db = _graph.CreateContext();
        var domain = $"t{Guid.CreateVersion7():N}"[..12];

        var loader = new PackLoader(db, new SystemClock());
        var result = await loader.LoadAsync(Pack(domain));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));

        var ids = db.Concepts
            .Where(c => c.Domain == domain)
            .ToDictionary(c => Bare(c.Slug, domain), c => c.Id, StringComparer.Ordinal);

        return (db, domain, ids);
    }

    private static string Pack(string d) => $$"""
        domain: {{d}}
        version: 1
        note: An asymmetric diamond, plus a contrast that must not order anything.
        concepts:
          - slug: tokenisation-{{d}}
            title: Tokenisation
            summary: Splitting text into tokens.
            difficulty: -0.8
            minutes: 30
            proof: { hold: 0.8, evidence: [{ type: Apply, min-score: 0.8, max: 0.9 }] }
          - slug: embeddings-{{d}}
            title: Embeddings
            summary: Dense vector representations.
            difficulty: -0.2
            minutes: 60
            requires: [tokenisation-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Apply, min-score: 0.8, max: 0.9 }] }
          - slug: bm25-{{d}}
            title: BM25
            summary: The lexical baseline a RAG system must beat.
            difficulty: 0.3
            minutes: 45
            requires: [tokenisation-{{d}}]
            contrasts: [retrieval-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Apply, min-score: 0.8, max: 0.9 }] }
          - slug: vector-index-{{d}}
            title: Vector index
            summary: HNSW and friends.
            difficulty: 0.5
            minutes: 75
            requires: [embeddings-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
          - slug: chunk-sizing-{{d}}
            title: Chunk sizing
            summary: Choosing how big a chunk should be.
            difficulty: 0.4
            minutes: 45
            requires: [embeddings-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
          - slug: windowing-{{d}}
            title: Windowing
            summary: Overlapping chunks so that meaning is not cut in half.
            difficulty: 0.5
            minutes: 40
            requires: [chunk-sizing-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
          - slug: retrieval-{{d}}
            title: Retrieval
            summary: Getting the right passages back.
            difficulty: 0.9
            minutes: 120
            requires: [vector-index-{{d}}, windowing-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
          - slug: reranking-{{d}}
            title: Reranking
            summary: Reordering candidates with a cross-encoder.
            difficulty: 1.2
            minutes: 90
            requires: [retrieval-{{d}}]
            proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
        """;
}
