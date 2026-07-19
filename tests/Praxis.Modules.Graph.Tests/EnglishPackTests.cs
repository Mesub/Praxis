using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// The second shipped pack, held to the same standard as the first: a wrong prerequisite does not
/// throw — it walks a learner into a wall weeks from now, and they blame themselves. So the
/// English pack is tested in CI exactly the way `SeedPackTests` tests ai-engineering.
public sealed class EnglishPackTests
{
    [Fact]
    public void The_english_pack_passes_the_validator()
    {
        var result = PackValidator.Validate(Document());

        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
    }

    [Fact]
    public void It_has_thirty_one_concepts()
        => Assert.Equal(31, Document().Concepts.Count);

    [Fact]
    public void Every_concept_can_actually_be_held()
    {
        // The mirror of the Evidence Ceiling: unfakeable evidence must be sufficient on its own,
        // or a learner could work forever and never finish.
        foreach (var concept in Document().Concepts)
        {
            var crossable = concept.Proof!.Evidence
                .Where(e => EvidenceTypes.CanCrossTheHoldThreshold.Contains(e.Type))
                .Sum(e => e.Max);

            Assert.True(
                crossable >= concept.Proof.Hold,
                $"'{concept.Slug}' cannot be held by unfakeable evidence alone "
                + $"({crossable:F2} against a threshold of {concept.Proof.Hold:F2}).");
        }
    }

    [Fact]
    public void The_nepali_transfer_beliefs_are_declared_by_name()
    {
        // The pack's sharpest feature is hunting first-language transfer errors by name. If these
        // keys drift, the item generator loses its targets silently.
        var keys = Document().Concepts
            .SelectMany(c => c.Misconceptions.Select(m => m.Key)).ToHashSet();

        Assert.Contains("articles-are-decoration", keys);
        Assert.Contains("perfect-is-fancy-past", keys);
        Assert.Contains("prepositions-translate-literally", keys);
        Assert.Contains("questions-by-tone-alone", keys);
        Assert.Equal(15, keys.Count);
    }

    private static string Yaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "english-everyday", "pack.yaml"));
    }

    private static PackDocument Document()
    {
        var parsed = PackLoader.Parse(Yaml());
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }
}
