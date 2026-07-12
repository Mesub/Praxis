using Microsoft.EntityFrameworkCore;
using Npgsql;
using Praxis.Modules.Graph.Domain;

namespace Praxis.Modules.Graph.Tests;

[Collection(SharedGraph.Name)]
public sealed class GraphPersistenceTests
{

    /// These tests scribble throwaway concepts into the database. They must NOT do it in
    /// "ai-engineering" — that is the real seed pack, and the pack's own tests count what is
    /// in it.
    private const string TestDomain = "graph-persistence-tests";
    private readonly GraphFixture _graph;

    public GraphPersistenceTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public async Task A_concept_and_its_prerequisite_survive_a_round_trip()
    {
        await using var db = _graph.CreateContext();

        var embeddings = Concept.Create(
            Slug("embeddings"), "Embeddings", "Dense vector representations of text.",
            TestDomain, -0.2, 60);

        var retrieval = Concept.Create(
            Slug("vector-retrieval"), "Vector retrieval", "Nearest-neighbour search over embeddings.",
            TestDomain, 0.6, 90);

        db.Concepts.AddRange(embeddings, retrieval);
        await db.SaveChangesAsync();

        db.Edges.Add(ConceptEdge.Create(embeddings.Id, retrieval.Id, EdgeKind.Requires, 1.0));
        await db.SaveChangesAsync();

        await using var fresh = _graph.CreateContext();

        var edge = await fresh.Edges.SingleAsync(e => e.ToConceptId == retrieval.Id);

        Assert.Equal(embeddings.Id, edge.FromConceptId);
        Assert.Equal(EdgeKind.Requires, edge.Kind);
        Assert.Equal(EdgeProvenance.Authored, edge.Provenance);
    }

    [Fact]
    public async Task Two_concepts_cannot_share_a_slug()
    {
        // The slug is how the pack, the item bank, and a piece of Evidence all refer to the same
        // idea. Two concepts sharing one would silently merge three different things.
        await using var db = _graph.CreateContext();

        var slug = Slug("chunking");

        db.Concepts.Add(Concept.Create(slug, "Chunking", "Splitting a corpus.", TestDomain, 0.3, 45));
        await db.SaveChangesAsync();

        db.Concepts.Add(Concept.Create(slug, "Chunking (again)", "A duplicate.", TestDomain, 0.3, 45));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.IsType<PostgresException>(ex.InnerException);
    }

    [Fact]
    public async Task The_same_prerequisite_cannot_be_declared_twice()
    {
        // A duplicate REQUIRES edge would double-count in the Compiler's path cost — the plan
        // would quietly overestimate the work and mis-order the route.
        await using var db = _graph.CreateContext();

        var a = Concept.Create(Slug("a"), "A", "A.", TestDomain, 0.0, 30);
        var b = Concept.Create(Slug("b"), "B", "B.", TestDomain, 0.0, 30);

        db.Concepts.AddRange(a, b);
        await db.SaveChangesAsync();

        db.Edges.Add(ConceptEdge.Create(a.Id, b.Id, EdgeKind.Requires));
        await db.SaveChangesAsync();

        db.Edges.Add(ConceptEdge.Create(a.Id, b.Id, EdgeKind.Requires));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_pair_may_hold_different_kinds_of_relationship()
    {
        // Precision and recall REQUIRE nothing of each other, but they are absolutely CONTRASTED
        // — and that is where the misconception lives.
        await using var db = _graph.CreateContext();

        var precision = Concept.Create(Slug("precision"), "Precision", "Of retrieved, how many were right.", TestDomain, 0.1, 20);
        var recall = Concept.Create(Slug("recall"), "Recall", "Of the right ones, how many were retrieved.", TestDomain, 0.1, 20);

        db.Concepts.AddRange(precision, recall);
        await db.SaveChangesAsync();

        db.Edges.Add(ConceptEdge.Create(precision.Id, recall.Id, EdgeKind.Contrasts, 1.0));
        db.Edges.Add(ConceptEdge.Create(precision.Id, recall.Id, EdgeKind.Relates, 0.8));

        await db.SaveChangesAsync();

        var edges = await db.Edges
            .Where(e => e.FromConceptId == precision.Id && e.ToConceptId == recall.Id)
            .ToListAsync();

        Assert.Equal(2, edges.Count);
    }

    [Fact]
    public async Task An_edge_cannot_point_at_a_concept_that_does_not_exist()
    {
        await using var db = _graph.CreateContext();

        var real = Concept.Create(Slug("real"), "Real", "Exists.", TestDomain, 0.0, 30);
        db.Concepts.Add(real);
        await db.SaveChangesAsync();

        db.Edges.Add(ConceptEdge.Create(real.Id, Guid.CreateVersion7(), EdgeKind.Requires));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_graph_version_records_what_the_graph_claimed_and_when()
    {
        // A piece of Evidence says "this learner demonstrated concept X". Years later a stranger
        // must be able to ask what X *meant* when that was claimed. Without this, improving the
        // graph silently rewrites the meaning of every credential ever issued against it.
        await using var db = _graph.CreateContext();

        var domain = $"test-{Guid.CreateVersion7():N}";

        db.Versions.Add(GraphVersion.Publish(
            domain, 1, "Initial hand-authored graph for the RAG objective.", 30, 41,
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        await db.SaveChangesAsync();

        var stored = await db.Versions.SingleAsync(v => v.Domain == domain);

        Assert.Equal(1, stored.Number);
        Assert.Equal(30, stored.ConceptCount);
        Assert.Equal(41, stored.EdgeCount);
    }

    [Fact]
    public async Task A_domain_cannot_publish_the_same_version_number_twice()
    {
        await using var db = _graph.CreateContext();

        var domain = $"test-{Guid.CreateVersion7():N}";
        var at = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        db.Versions.Add(GraphVersion.Publish(domain, 1, "First.", 10, 12, at));
        await db.SaveChangesAsync();

        db.Versions.Add(GraphVersion.Publish(domain, 1, "Also first, apparently.", 11, 13, at));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// Slugs are globally unique, and these tests share a database.
    private static string Slug(string name) => $"{name}-{Guid.CreateVersion7():N}";
}
