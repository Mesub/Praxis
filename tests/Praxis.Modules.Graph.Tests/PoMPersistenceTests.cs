using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

[Collection(SharedGraph.Name)]
public sealed class PoMPersistenceTests
{

    /// These tests scribble throwaway concepts into the database. They must NOT do it in
    /// "ai-engineering" — that is the real seed pack, and the pack's own tests count what is
    /// in it.
    private const string TestDomain = "graph-persistence-tests";
    private readonly GraphFixture _graph;

    public PoMPersistenceTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public async Task A_contract_and_its_clauses_survive_a_round_trip()
    {
        await using var db = _graph.CreateContext();

        var concept = Concept.Create(
            $"rag-eval-{Guid.CreateVersion7():N}", "Evaluating a RAG system",
            "Measuring retrieval and generation quality against a baseline.",
            TestDomain, 0.9, 120);

        db.Concepts.Add(concept);
        await db.SaveChangesAsync();

        db.Contracts.Add(PoMContract.Create(
            concept.Id,
            holdThreshold: 0.80,
            accepted:
            [
                AcceptedEvidence.Of(EvidenceType.Explain, 0.7, 0.25),
                AcceptedEvidence.Of(EvidenceType.Apply, 0.8, 0.50, requiredCount: 2),
                AcceptedEvidence.Of(EvidenceType.Build, 1.0, 0.60),
            ]));

        await db.SaveChangesAsync();

        await using var fresh = _graph.CreateContext();

        var stored = await fresh.Contracts.SingleAsync(c => c.ConceptId == concept.Id);

        Assert.Equal(0.80, stored.HoldThreshold);
        Assert.Equal(3, stored.Accepted.Count);

        var apply = stored.ClauseFor(EvidenceType.Apply);
        Assert.NotNull(apply);
        Assert.Equal(2, apply.RequiredCount);
        Assert.Equal(0.50, apply.MaxContribution);

        // The ceiling survives the round trip, because it is computed from what is stored.
        Assert.True(stored.ModelGradedCeiling < stored.HoldThreshold);
    }

    [Fact]
    public async Task A_concept_can_only_have_one_contract()
    {
        // Two contracts would be two different answers to "what would prove you understand this",
        // and the Twin would have to pick one.
        await using var db = _graph.CreateContext();

        var concept = Concept.Create(
            $"dupe-{Guid.CreateVersion7():N}", "Dupe", "One contract only.", TestDomain, 0.0, 30);

        db.Concepts.Add(concept);
        await db.SaveChangesAsync();

        db.Contracts.Add(PoMContract.Create(
            concept.Id, 0.8, [AcceptedEvidence.Of(EvidenceType.Build, 1.0, 0.9)]));
        await db.SaveChangesAsync();

        db.Contracts.Add(PoMContract.Create(
            concept.Id, 0.7, [AcceptedEvidence.Of(EvidenceType.Apply, 0.8, 0.9)]));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_contract_cannot_exist_without_its_concept()
    {
        await using var db = _graph.CreateContext();

        db.Contracts.Add(PoMContract.Create(
            Guid.CreateVersion7(), 0.8, [AcceptedEvidence.Of(EvidenceType.Build, 1.0, 0.9)]));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
