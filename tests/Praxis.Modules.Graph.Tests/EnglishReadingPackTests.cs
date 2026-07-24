using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// The third shipped pack — reading comprehension — held to the same CI standard as the first
/// two. A wrong prerequisite does not throw; it walks a learner into a wall weeks from now and
/// they blame themselves. So the reading pack is validated in CI exactly the way the
/// ai-engineering and everyday-English packs are.
public sealed class EnglishReadingPackTests
{
    [Fact]
    public void The_reading_pack_passes_the_validator()
    {
        var result = PackValidator.Validate(Document());

        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
    }

    [Fact]
    public void It_has_fifteen_concepts()
        => Assert.Equal(15, Document().Concepts.Count);

    [Fact]
    public void Every_concept_can_actually_be_held()
    {
        // The mirror of the Evidence Ceiling: unfakeable evidence must be sufficient on its own,
        // or a learner could work forever and never finish. For reading, the unfakeable evidence
        // is a never-seen passage answered against a fixed key.
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
    public void The_reading_habit_misconceptions_are_declared_by_name()
    {
        // The pack hunts reading-habit transfer errors by name — the same distractor traps a
        // Nepali reader falls into. If these keys drift, the item generator loses its targets.
        var keys = Document().Concepts
            .SelectMany(c => c.Misconceptions.Select(m => m.Key)).ToHashSet();

        Assert.Contains("keyword-match-wins", keys);
        Assert.Contains("literal-only", keys);
        Assert.Contains("nearest-noun-reference", keys);
        Assert.Contains("every-word-or-fail", keys);
        Assert.Equal(8, keys.Count);
    }

    private static string Yaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "english-reading", "pack.yaml"));
    }

    private static PackDocument Document()
    {
        var parsed = PackLoader.Parse(Yaml());
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }
}
