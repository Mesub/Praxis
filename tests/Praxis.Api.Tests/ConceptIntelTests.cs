using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Assessment;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The seam between the Graph and Assessment's item generator.**
///
/// Assessment asks for a concept's summary, difficulty target and declared wrong beliefs through
/// `IConceptIntel` — the same shape as the tutor's `IMisconceptionLibrary` (M13.2b-ii). Neither
/// module references the other; `GraphConceptIntel`, resolved from the real host's DI container,
/// is the only thing that knows both exist.
[Collection(SharedApi.Name)]
public sealed class ConceptIntelTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public ConceptIntelTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        // The real shipped pack, so `bm25` and its declared misconception actually exist to ask
        // about. Loading is idempotent — another test class may already have loaded it.
        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();

        var result = await loader.LoadAsync(SeedPack());

        Assert.True(
            result.IsSuccess || result.Status == ResultStatus.Conflict,
            string.Join("\n", result.Errors));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_known_concept_returns_its_summary_difficulty_and_declared_misconceptions()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        var bm25 = graph.Concepts.Single(c => c.Slug == "bm25");

        var intel = scope.ServiceProvider.GetRequiredService<IConceptIntel>();

        var result = await intel.ForAsync(bm25.Id);

        Assert.NotNull(result);
        Assert.Equal(bm25.Summary, result!.Summary);

        // The pack's prior is an IRT logit; the generator's scale is 0..1. bm25's prior (-0.2)
        // maps to somewhere in the open interval, not pinned to either edge.
        Assert.True(result.DifficultyTarget is > 0.0 and < 1.0);

        Assert.Contains("bm25-is-obsolete", result.DeclaredMisconceptions.Keys);
    }

    [Fact]
    public async Task An_unknown_concept_returns_null_not_an_invented_answer()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var intel = scope.ServiceProvider.GetRequiredService<IConceptIntel>();

        var result = await intel.ForAsync(Guid.NewGuid());

        Assert.Null(result);
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
