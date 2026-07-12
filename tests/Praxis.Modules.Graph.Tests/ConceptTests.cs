using Praxis.Modules.Graph.Domain;

namespace Praxis.Modules.Graph.Tests;

public sealed class ConceptTests
{
    [Fact]
    public void A_concept_normalises_its_slug_and_domain()
    {
        var concept = Concept.Create(
            "  Chunking-Strategies  ", "  Chunking strategies  ",
            "  How a corpus is split before embedding.  ",
            "  AI-Engineering  ", 0.4, 45);

        Assert.Equal("chunking-strategies", concept.Slug);
        Assert.Equal("ai-engineering", concept.Domain);
        Assert.Equal("Chunking strategies", concept.Title);
        Assert.Equal("How a corpus is split before embedding.", concept.Summary);
    }

    [Theory]
    [InlineData(-4.5)]
    [InlineData(4.5)]
    [InlineData(75)]     // someone typed a percentage
    public void Difficulty_outside_the_logit_scale_is_refused(double difficulty)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Concept.Create("x", "X", "x", "d", difficulty, 30));

        Assert.Contains("percentage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void A_concept_that_takes_no_time_is_refused(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Concept.Create("x", "X", "x", "d", 0.0, minutes));
    }

    [Fact]
    public void Difficulty_and_duration_are_priors_and_can_be_replaced_by_measurement()
    {
        // The point of R-7: an estimate that reality never corrects is an opinion with a
        // decimal point.
        var concept = Concept.Create("bm25", "BM25", "A lexical baseline.", "ai-engineering", 0.0, 60);

        concept.Recalibrate(difficulty: 1.4, estimatedMinutes: 95);

        Assert.Equal(1.4, concept.DifficultyPrior);
        Assert.Equal(95, concept.EstimatedMinutes);
    }
}

public sealed class ConceptEdgeTests
{
    [Fact]
    public void A_concept_cannot_require_itself()
    {
        var id = Guid.CreateVersion7();

        var ex = Assert.Throws<ArgumentException>(
            () => ConceptEdge.Create(id, id, EdgeKind.Requires));

        Assert.Contains("itself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void Edge_strength_is_a_weight_in_zero_to_one(double strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConceptEdge.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), EdgeKind.Requires, strength));
    }

    [Fact]
    public void An_edge_records_where_it_came_from()
    {
        // An edge a human authored and an edge a model guessed are not the same claim, and the
        // graph must never pretend otherwise (R-2).
        var authored = ConceptEdge.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), EdgeKind.Requires,
            provenance: EdgeProvenance.Authored);

        var guessed = ConceptEdge.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), EdgeKind.Requires,
            provenance: EdgeProvenance.Inferred);

        var learned = ConceptEdge.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), EdgeKind.Requires,
            provenance: EdgeProvenance.Discovered);

        Assert.Equal(EdgeProvenance.Authored, authored.Provenance);
        Assert.Equal(EdgeProvenance.Inferred, guessed.Provenance);
        Assert.Equal(EdgeProvenance.Discovered, learned.Provenance);
    }

    /// Only REQUIRES constrains order. If a second edge kind ever becomes a dependency, the
    /// Compiler's cycle detection and path cost both silently become wrong.
    [Fact]
    public void There_are_four_edge_kinds_and_only_one_of_them_orders_anything()
    {
        var kinds = Enum.GetValues<EdgeKind>();

        Assert.Equal(4, kinds.Length);
        Assert.Contains(EdgeKind.Requires, kinds);
        Assert.Contains(EdgeKind.Relates, kinds);
        Assert.Contains(EdgeKind.Contrasts, kinds);
        Assert.Contains(EdgeKind.AppliesTo, kinds);
    }
}
