using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Persistence;
using Praxis.Shared;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Praxis.Modules.Graph.Packs;

public sealed class PackLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly GraphDbContext _db;
    private readonly IClock _clock;

    public PackLoader(GraphDbContext db, IClock clock) => (_db, _clock) = (db, clock);

    public static Result<PackDocument> Parse(string yaml)
    {
        try
        {
            var document = Yaml.Deserialize<PackDocument>(yaml);

            return document is null
                ? Result<PackDocument>.Invalid("The pack file is empty.")
                : Result<PackDocument>.Success(document);
        }
        catch (YamlException ex)
        {
            return Result<PackDocument>.Invalid(
                $"The pack is not valid YAML at line {ex.Start.Line}: {ex.Message}");
        }
    }

    /// Parse, validate, and only then persist. A pack that fails the gate never touches the graph.
    ///
    /// Idempotent by slug: loading the same pack twice updates in place rather than duplicating,
    /// so re-running a load after fixing one line does not corrupt the graph.
    public async Task<Result<LoadedPack>> LoadAsync(string yaml, CancellationToken ct = default)
    {
        var parsed = Parse(yaml);

        if (!parsed.IsSuccess)
        {
            return Result<LoadedPack>.Invalid(parsed.Message ?? "Could not parse the pack.", parsed.Errors);
        }

        var validated = PackValidator.Validate(parsed.Value);

        if (!validated.IsSuccess)
        {
            return Result<LoadedPack>.Invalid(validated.Message ?? "The pack is invalid.", validated.Errors);
        }

        return await PersistAsync(validated.Value, ct);
    }

    private async Task<Result<LoadedPack>> PersistAsync(ValidatedPack pack, CancellationToken ct)
    {
        var document = pack.Document;
        var domain = document.Domain.Trim().ToLowerInvariant();

        if (await _db.Versions.AnyAsync(v => v.Domain == domain && v.Number == document.Version, ct))
        {
            return Result<LoadedPack>.Conflict(
                $"{domain} v{document.Version} has already been published. Bump the version — an "
                + "old credential's meaning depends on the graph it was issued against.");
        }

        var slugs = pack.BySlug.Keys.ToList();

        var existing = await _db.Concepts
            .Where(c => slugs.Contains(c.Slug))
            .ToDictionaryAsync(c => c.Slug, StringComparer.Ordinal, ct);

        var resolved = new Dictionary<string, Concept>(StringComparer.OrdinalIgnoreCase);

        foreach (var (slug, source) in pack.BySlug)
        {
            if (existing.TryGetValue(slug, out var concept))
            {
                concept.Recalibrate(source.Difficulty, source.Minutes);
            }
            else
            {
                concept = Concept.Create(
                    slug, source.Title, source.Summary, domain, source.Difficulty, source.Minutes);

                _db.Concepts.Add(concept);
            }

            resolved[slug] = concept;
        }

        await _db.SaveChangesAsync(ct);

        // Replace this domain's edges and contracts wholesale. A pack is a complete statement of
        // what the domain claims; merging would leave behind edges the author has since deleted,
        // and a deleted prerequisite that lingers is exactly as harmful as one that was never there.
        var conceptIds = resolved.Values.Select(c => c.Id).ToList();

        var staleEdges = await _db.Edges
            .Where(e => conceptIds.Contains(e.FromConceptId) || conceptIds.Contains(e.ToConceptId))
            .ToListAsync(ct);

        _db.Edges.RemoveRange(staleEdges);

        var staleContracts = await _db.Contracts
            .Where(c => conceptIds.Contains(c.ConceptId))
            .ToListAsync(ct);

        _db.Contracts.RemoveRange(staleContracts);

        // Misconceptions go the same way: a pack is a complete statement of the domain's declared
        // wrong beliefs, so a belief the author has since deleted must not linger in the graph.
        var staleMisconceptions = await _db.Misconceptions
            .Where(m => conceptIds.Contains(m.ConceptId))
            .ToListAsync(ct);

        _db.Misconceptions.RemoveRange(staleMisconceptions);
        await _db.SaveChangesAsync(ct);

        var edgeCount = 0;

        foreach (var (slug, source) in pack.BySlug)
        {
            var from = resolved[slug];

            edgeCount += AddEdges(from, source.Requires, EdgeKind.Requires, resolved);
            edgeCount += AddEdges(from, source.Contrasts, EdgeKind.Contrasts, resolved);
            edgeCount += AddEdges(from, source.Relates, EdgeKind.Relates, resolved);
            edgeCount += AddEdges(from, source.AppliesTo, EdgeKind.AppliesTo, resolved);

            var proof = source.Proof!;

            _db.Contracts.Add(PoMContract.Create(
                from.Id,
                proof.Hold,
                [.. proof.Evidence.Select(e => AcceptedEvidence.Of(e.Type, e.MinScore, e.Max, e.Count, e.Format))]));

            foreach (var m in source.Misconceptions)
            {
                _db.Misconceptions.Add(ConceptMisconception.Create(from.Id, m.Key, m.Statement));
            }
        }

        _db.Versions.Add(GraphVersion.Publish(
            domain,
            document.Version,
            document.Note,
            resolved.Count,
            edgeCount,
            _clock.UtcNow));

        await _db.SaveChangesAsync(ct);

        return Result<LoadedPack>.Success(
            new LoadedPack(domain, document.Version, resolved.Count, edgeCount),
            $"Loaded {domain} v{document.Version}: {resolved.Count} concepts, {edgeCount} edges.");
    }

    /// A REQUIRES edge in the pack reads "this concept requires that one", so the dependency
    /// points *from* the prerequisite *to* the dependent — the direction the Compiler walks.
    private int AddEdges(
        Concept from,
        List<string> targets,
        EdgeKind kind,
        Dictionary<string, Concept> resolved)
    {
        var added = 0;

        foreach (var target in targets.Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal))
        {
            var to = resolved[target];

            var edge = kind == EdgeKind.Requires
                ? ConceptEdge.Create(to.Id, from.Id, EdgeKind.Requires)
                : ConceptEdge.Create(from.Id, to.Id, kind);

            _db.Edges.Add(edge);
            added++;
        }

        return added;
    }
}

public sealed record LoadedPack(string Domain, int Version, int Concepts, int Edges);
