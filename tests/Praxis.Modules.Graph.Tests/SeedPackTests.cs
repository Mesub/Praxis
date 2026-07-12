using Praxis.Modules.Graph.Packs;
using Praxis.Modules.Graph.Queries;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// The real pack, not a fixture. `packs/ai-engineering/pack.yaml` is the curriculum a learner will
/// actually be given, and it is the first place a plausible-but-wrong graph would do damage.
///
/// A wrong prerequisite does not throw. It walks someone into a wall six weeks from now, and they
/// blame themselves. So the pack is held to the same standard as the code.
[Collection(SharedGraph.Name)]
public sealed class SeedPackTests
{
    private const string Domain = "ai-engineering";

    private readonly GraphFixture _graph;

    public SeedPackTests(GraphFixture graph) => _graph = graph;

    [Fact]
    public void The_seed_pack_passes_the_validator()
    {
        var result = PackValidator.Validate(Document());

        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
    }

    [Fact]
    public void It_has_thirty_concepts()
    {
        // Phase 0 ships 30 (M2.5), growing to 180 at M13.
        Assert.Equal(30, Document().Concepts.Count);
    }

    [Fact]
    public void Every_concept_can_actually_be_held()
    {
        // The Evidence Ceiling refuses a contract where model-graded evidence could reach the
        // threshold. The mirror failure is a concept nobody can ever finish — so every one of
        // these must offer at least one thing a model cannot fake.
        foreach (var concept in Document().Concepts)
        {
            var crossable = concept.Proof!.Evidence
                .Where(e => EvidenceTypes.CanCrossTheHoldThreshold.Contains(e.Type))
                .Sum(e => e.Max);

            Assert.True(
                crossable >= concept.Proof.Hold,
                $"'{concept.Slug}' cannot be held even by producing every piece of unfakeable "
                + $"evidence it accepts ({crossable:F2} against a threshold of {concept.Proof.Hold:F2}). "
                + "A learner could work on it forever and never finish it.");
        }
    }

    [Fact]
    public void The_objective_demands_something_that_ran()
    {
        // "Beating the baseline" is the build target of the whole pack. It cannot be talked into
        // existence, and its contract has to say so.
        var target = Document().Concepts.Single(c => c.Slug == "beating-the-baseline");

        Assert.Contains(target.Proof!.Evidence, e => e.Type == EvidenceType.Build);
        Assert.Equal(0.90, target.Proof.Hold);

        var modelGraded = target.Proof.Evidence
            .Where(e => e.Type.IsCeilingBound())
            .Sum(e => e.Max);

        Assert.True(modelGraded < target.Proof.Hold);
    }

    [Fact]
    public async Task It_loads_into_a_real_graph()
    {
        await using var db = _graph.CreateContext();
        var loader = new PackLoader(db, new SystemClock());

        var result = await loader.LoadAsync(Yaml());

        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
        Assert.Equal(30, result.Value.Concepts);
        Assert.True(result.Value.Edges >= 30, $"Only {result.Value.Edges} edges — that graph is too sparse to be real.");
    }

    [Fact]
    public async Task A_learner_who_knows_nothing_has_exactly_one_place_to_start()
    {
        // The graph has one root: tokenisation. Two roots would mean the pack quietly offers a
        // choice it never explained.
        await using var db = _graph.CreateContext();
        await new PackLoader(db, new SystemClock()).LoadAsync(Yaml());

        var frontier = await new GraphQueries(db).FrontierAsync(Domain, []);

        Assert.Single(frontier);
        Assert.Equal("tokenisation", frontier[0].Slug);
    }

    [Fact]
    public async Task The_objective_is_reachable_from_nothing()
    {
        // The most important property of the whole pack: a learner starting from zero can, by
        // holding one concept at a time, arrive at the build target. If the objective is not
        // reachable, the pack is a wish.
        await using var db = _graph.CreateContext();
        await new PackLoader(db, new SystemClock()).LoadAsync(Yaml());

        var queries = new GraphQueries(db);
        var held = new List<Guid>();
        var order = new List<string>();

        // Walk the frontier greedily, exactly as a learner would.
        for (var step = 0; step < 100; step++)
        {
            var frontier = await queries.FrontierAsync(Domain, held);

            if (frontier.Count == 0)
            {
                break;
            }

            var next = frontier[0];
            held.Add(next.Id);
            order.Add(next.Slug);
        }

        Assert.Equal(30, order.Count);
        Assert.Contains("beating-the-baseline", order, StringComparer.Ordinal);

        // And it is last, because everything in the pack exists to serve it.
        Assert.Equal("beating-the-baseline", order[^1]);
    }

    [Fact]
    public async Task The_baseline_must_be_measured_before_it_can_be_beaten()
    {
        // The pedagogical spine of the pack. You cannot claim to have beaten BM25 if you never ran
        // BM25 — and a distressing number of real RAG systems never did.
        await using var db = _graph.CreateContext();
        await new PackLoader(db, new SystemClock()).LoadAsync(Yaml());

        var queries = new GraphQueries(db);

        var target = db.Concepts.Single(c => c.Slug == "beating-the-baseline");
        var prereqs = await queries.PrerequisitesOfAsync(target.Id);

        var slugs = prereqs.Select(c => c.Slug).ToList();

        Assert.Contains("the-bm25-baseline-run", slugs, StringComparer.Ordinal);
        Assert.Contains("building-an-eval-set", slugs, StringComparer.Ordinal);
        Assert.Contains("the-eval-trap", slugs, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Nothing_downstream_can_recover_what_retrieval_never_surfaced()
    {
        // Generation depends on retrieval, and the graph should say so. If a learner could reach
        // grounded-generation without holding retrieval, the pack would be teaching them to polish
        // an answer built on passages they never learned to find.
        await using var db = _graph.CreateContext();
        await new PackLoader(db, new SystemClock()).LoadAsync(Yaml());

        var generation = db.Concepts.Single(c => c.Slug == "grounded-generation");
        var prereqs = await new GraphQueries(db).PrerequisitesOfAsync(generation.Id);

        Assert.Contains("dense-retrieval", prereqs.Select(c => c.Slug), StringComparer.Ordinal);
        Assert.Contains("reranking", prereqs.Select(c => c.Slug), StringComparer.Ordinal);
    }

    private static string Yaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        return File.ReadAllText(Path.Combine(root.FullName, "packs", "ai-engineering", "pack.yaml"));
    }

    private static PackDocument Document()
    {
        var parsed = PackLoader.Parse(Yaml());
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }
}
