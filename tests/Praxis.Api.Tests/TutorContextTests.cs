using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Tutor;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// **The seam between Assessment's item bank, the Graph's concepts, and the tutor** (M17).
///
/// `TutorContext` (`src/Praxis.Api/Composition/TutorContext.cs`) is the one place allowed to hold
/// both `AssessmentDbContext` and `GraphDbContext` — exactly the shape of `GraphConceptIntel`
/// (`ConceptIntelTests.cs`), which this file is modelled on. Before this test, both of its queries
/// had only ever been exercised through a hand-written stub in `TakeHintTests.cs`; the actual EF
/// queries against real Postgres — the item schema's `Passage`/`Stem` columns, the graph schema's
/// `Summary` column, and the two null cases the hint path depends on to fall back cleanly — had
/// never run.
[Collection(SharedApi.Name)]
public sealed class TutorContextTests : IAsyncLifetime
{
    private readonly ApiFixture _api;

    public TutorContextTests(ApiFixture api) => _api = api;

    public async Task InitializeAsync()
    {
        // The real shipped pack, so a concept with a genuine summary exists to ask about. Loading
        // is idempotent — another class in this collection may already have loaded it.
        await using var scope = _api.Factory.Services.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetRequiredService<PackLoader>();

        var result = await loader.LoadAsync(SeedPack());

        Assert.True(
            result.IsSuccess || result.Status == ResultStatus.Conflict,
            string.Join("\n", result.Errors));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ForItemAsync_returns_the_real_stem_and_passage()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var itemId = await BankReadingItem(scope.ServiceProvider);

        var context = scope.ServiceProvider.GetRequiredService<ITutorContext>();

        var result = await context.ForItemAsync(itemId);

        Assert.NotNull(result);
        Assert.Equal("Whose roster is the system most likely to get wrong?", result!.Stem);
        Assert.Contains("However", result.Passage);
    }

    [Fact]
    public async Task SummaryForAsync_returns_the_real_concept_summary()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var graph = scope.ServiceProvider.GetRequiredService<GraphDbContext>();
        var bm25 = graph.Concepts.Single(c => c.Slug == "bm25");

        var context = scope.ServiceProvider.GetRequiredService<ITutorContext>();

        var summary = await context.SummaryForAsync(bm25.Id);

        Assert.Equal(bm25.Summary, summary);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    /// **The fallback the hint path depends on.** `TakeHint`/`StreamHint` treat a null item as "this
    /// session was never bound to one" and fall back to "the current problem" — that only stays
    /// honest if an unknown id genuinely returns null rather than throwing or inventing a row.
    [Fact]
    public async Task An_unknown_item_returns_null_not_an_invented_stem()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<ITutorContext>();

        var result = await context.ForItemAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task An_unknown_concept_returns_null_not_an_invented_summary()
    {
        await using var scope = _api.Factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<ITutorContext>();

        var summary = await context.SummaryForAsync(Guid.NewGuid());

        Assert.Null(summary);
    }

    private static async Task<Guid> BankReadingItem(IServiceProvider services)
    {
        var db = services.GetRequiredService<AssessmentDbContext>();

        var id = Guid.CreateVersion7();

        var item = BankedItem.From(
            id,
            Guid.CreateVersion7(),
            EvidenceType.Apply,
            stem: "Whose roster is the system most likely to get wrong?",
            fingerprint: id.ToString("N"),
            difficulty: 0.0,
            guessing: 0.25,
            format: ItemFormat.MultipleChoice,
            passage: "The new roster system goes live on Monday. However, if you swapped a shift "
                + "with a colleague before the changeover, the roster may still show your old slot.",
            options:
            [
                "Everyone who works a Monday shift.",
                "Only staff who never swapped a shift.",
                "Anyone whose roster still shows the old slot after swapping.",
                "Nobody — the memo names no one in particular.",
            ]);

        db.Items.Add(item);
        await db.SaveChangesAsync();

        return id;
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
