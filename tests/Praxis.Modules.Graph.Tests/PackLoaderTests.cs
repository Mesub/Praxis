using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

[Collection(SharedGraph.Name)]
public sealed class PackLoaderTests
{
    private readonly GraphFixture _graph;

    public PackLoaderTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public async Task A_valid_pack_loads_into_the_graph()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();
        var result = await loader.LoadAsync(Pack(domain, version: 1));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));
        Assert.Equal(3, result.Value.Concepts);
        Assert.Equal(2, result.Value.Edges);   // one REQUIRES, one CONTRASTS

        var concepts = await db.Concepts.Where(c => c.Domain == domain).ToListAsync();
        Assert.Equal(3, concepts.Count);

        var version = await db.Versions.SingleAsync(v => v.Domain == domain);
        Assert.Equal(1, version.Number);
        Assert.Equal(3, version.ConceptCount);
    }

    [Fact]
    public async Task A_requires_edge_points_from_the_prerequisite_to_the_dependent()
    {
        // The direction the Compiler walks. "retrieval requires embeddings" becomes an edge FROM
        // embeddings TO retrieval — get this backwards and every plan is reversed.
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();
        await loader.LoadAsync(Pack(domain, 1));

        var embeddings = await db.Concepts.SingleAsync(c => c.Domain == domain && c.Slug.StartsWith("embeddings"));
        var retrieval = await db.Concepts.SingleAsync(c => c.Domain == domain && c.Slug.StartsWith("vector-retrieval"));

        var edge = await db.Edges.SingleAsync(e => e.Kind == EdgeKind.Requires
                                                   && e.ToConceptId == retrieval.Id);

        Assert.Equal(embeddings.Id, edge.FromConceptId);
    }

    [Fact]
    public async Task Every_loaded_concept_has_an_honest_contract()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();
        await loader.LoadAsync(Pack(domain, 1));

        var conceptIds = await db.Concepts.Where(c => c.Domain == domain).Select(c => c.Id).ToListAsync();
        var contracts = await db.Contracts.Where(c => conceptIds.Contains(c.ConceptId)).ToListAsync();

        Assert.Equal(3, contracts.Count);

        // Every one of them: a model's opinion alone cannot reach the threshold.
        Assert.All(contracts, c => Assert.True(c.ModelGradedCeiling < c.HoldThreshold));
    }

    [Fact]
    public async Task An_invalid_pack_never_touches_the_graph()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();

        var before = await db.Concepts.CountAsync();

        var result = await loader.LoadAsync($$"""
            domain: {{domain}}
            version: 1
            note: A cycle.
            concepts:
              - slug: {{domain}}-a
                title: A
                summary: A.
                difficulty: 0
                minutes: 30
                requires: [{{domain}}-b]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
              - slug: {{domain}}-b
                title: B
                summary: B.
                difficulty: 0
                minutes: 30
                requires: [{{domain}}-a]
                proof: { hold: 0.8, evidence: [{ type: Build, min-score: 1.0, max: 0.9 }] }
            """);

        Assert.Equal(ResultStatus.Invalid, result.Status);

        // Nothing was written. The gate is before the graph, not after it.
        Assert.Equal(before, await db.Concepts.CountAsync());
        Assert.Empty(await db.Versions.Where(v => v.Domain == domain).ToListAsync());
    }

    [Fact]
    public async Task Loading_the_same_version_twice_is_refused()
    {
        // An old credential's meaning depends on the graph it was issued against. Republishing a
        // version silently rewrites history.
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();

        Assert.True((await loader.LoadAsync(Pack(domain, 1))).IsSuccess);

        var second = await loader.LoadAsync(Pack(domain, 1));

        Assert.Equal(ResultStatus.Conflict, second.Status);
        Assert.Contains("Bump the version", second.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reloading_at_a_new_version_updates_in_place_and_does_not_duplicate()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();

        await loader.LoadAsync(Pack(domain, 1));
        var result = await loader.LoadAsync(Pack(domain, 2, minutes: 120));

        Assert.True(result.IsSuccess, string.Join(" | ", result.Errors));

        // Three concepts, not six.
        Assert.Equal(3, await db.Concepts.CountAsync(c => c.Domain == domain));

        // And the recalibrated estimate took.
        var embeddings = await db.Concepts.SingleAsync(c => c.Domain == domain && c.Slug.StartsWith("embeddings"));
        Assert.Equal(120, embeddings.EstimatedMinutes);

        // Two versions, both preserved: what the graph claimed then, and what it claims now.
        Assert.Equal(2, await db.Versions.CountAsync(v => v.Domain == domain));
    }

    [Fact]
    public async Task A_reload_removes_an_edge_the_author_deleted()
    {
        // A prerequisite that lingers after being deleted is exactly as harmful as one that was
        // never authored. A pack is a complete statement, not a patch.
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var domain = Domain();

        // v1: bm25 CONTRASTS retrieval and RELATES embeddings. Three edges, with the REQUIRES.
        var v1 = await loader.LoadAsync(Pack(domain, 1, alsoRelates: true));
        Assert.True(v1.IsSuccess, string.Join(" | ", v1.Errors));
        Assert.Equal(3, await CountEdges(db, domain));

        // v2: the author deleted the RELATES edge.
        var v2 = await loader.LoadAsync(Pack(domain, 2, alsoRelates: false));
        Assert.True(v2.IsSuccess, string.Join(" | ", v2.Errors));

        Assert.Equal(2, await CountEdges(db, domain));
    }

    private static async Task<int> CountEdges(Persistence.GraphDbContext db, string domain)
    {
        var ids = await db.Concepts.Where(c => c.Domain == domain).Select(c => c.Id).ToListAsync();

        return await db.Edges.CountAsync(e => ids.Contains(e.FromConceptId) || ids.Contains(e.ToConceptId));
    }

    /// Slugs are globally unique and these tests share a database, so each gets its own namespace.
    private static string Domain() => $"t{Guid.CreateVersion7():N}"[..12];

    private static string Pack(string domain, int version, int minutes = 60, bool alsoRelates = false)
    {
        // The raw literal below is de-indented to its closing quotes, so by the time this is
        // substituted the surrounding YAML keys sit at 4 spaces. A second line has to match that,
        // or the document quietly reshapes itself into something else.
        var lastEdge = alsoRelates
            ? $"contrasts: [vector-retrieval-{domain}]\n    relates: [embeddings-{domain}]"
            : $"contrasts: [vector-retrieval-{domain}]";

        return $$"""
            domain: {{domain}}
            version: {{version}}
            note: Test pack v{{version}}.
            concepts:
              - slug: embeddings-{{domain}}
                title: Embeddings
                summary: Dense vector representations of text.
                difficulty: -0.2
                minutes: {{minutes}}
                proof:
                  hold: 0.80
                  evidence:
                    - { type: Explain, min-score: 0.7, max: 0.25 }
                    - { type: Apply, min-score: 0.8, max: 0.80, count: 2 }
              - slug: vector-retrieval-{{domain}}
                title: Vector retrieval
                summary: Nearest-neighbour search over embeddings.
                difficulty: 0.6
                minutes: 90
                requires: [embeddings-{{domain}}]
                proof:
                  hold: 0.80
                  evidence:
                    - { type: Build, min-score: 1.0, max: 0.85 }
                    - { type: Explain, min-score: 0.7, max: 0.20 }
              - slug: bm25-{{domain}}
                title: BM25
                summary: The lexical baseline a RAG system must actually beat.
                difficulty: 0.3
                minutes: 45
                {{lastEdge}}
                proof:
                  hold: 0.80
                  evidence:
                    - { type: Apply, min-score: 0.8, max: 0.80 }
                    - { type: Recall, min-score: 0.9, max: 0.15 }
            """;
    }
}
