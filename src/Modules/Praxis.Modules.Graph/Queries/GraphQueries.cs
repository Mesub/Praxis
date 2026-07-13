using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;
using Praxis.Modules.Graph.Persistence;

namespace Praxis.Modules.Graph.Queries;

/// Walking the dependency DAG.
///
/// Recursive SQL, not a graph database. CANON §5.4 names Apache AGE, and it will earn its place
/// when a traversal actually hurts — at 180 concepts, a recursive CTE is faster than the network
/// hop to a graph engine, and it is one fewer thing to run.
///
/// Only `REQUIRES` is walked. It is the sole edge that orders anything; treating RELATES or
/// CONTRASTS as a dependency would invent prerequisites nobody authored.
public sealed class GraphQueries
{
    private readonly GraphDbContext _db;

    public GraphQueries(GraphDbContext db) => _db = db;

    /// The concept's **declared misconception library** — the named wrong beliefs a learner might
    /// hold about it. Handed to the AI extractor as its closed match set (I-6), so a wrong answer is
    /// matched against these before anything new is ever invented.
    public async Task<IReadOnlyList<ConceptMisconception>> MisconceptionsOfAsync(
        Guid conceptId, CancellationToken ct = default)
        => await _db.Misconceptions
            .AsNoTracking()
            .Where(m => m.ConceptId == conceptId)
            .OrderBy(m => m.Key)
            .ToListAsync(ct);

    /// Everything this concept transitively depends on. What you must already hold to hold it.
    public async Task<IReadOnlyList<Concept>> PrerequisitesOfAsync(
        Guid conceptId, CancellationToken ct = default)
    {
        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE prereqs(id) AS (
                    SELECT e."FromConceptId"
                    FROM graph.concept_edges e
                    WHERE e."ToConceptId" = {conceptId}
                      AND e."Kind" = 1
                      AND NOT e."IsDeleted"

                    UNION

                    SELECT e."FromConceptId"
                    FROM graph.concept_edges e
                    JOIN prereqs p ON e."ToConceptId" = p.id
                    WHERE e."Kind" = 1
                      AND NOT e."IsDeleted"
                )
                SELECT id AS "Value" FROM prereqs
                """)
            .ToListAsync(ct);

        return await ConceptsByIdAsync(ids, ct);
    }

    /// Everything that transitively depends on this concept. What breaks — or never unlocks —
    /// if the learner never holds it. The answer to "what does skipping linear algebra cost me".
    public async Task<IReadOnlyList<Concept>> DependentsOfAsync(
        Guid conceptId, CancellationToken ct = default)
    {
        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE dependents(id) AS (
                    SELECT e."ToConceptId"
                    FROM graph.concept_edges e
                    WHERE e."FromConceptId" = {conceptId}
                      AND e."Kind" = 1
                      AND NOT e."IsDeleted"

                    UNION

                    SELECT e."ToConceptId"
                    FROM graph.concept_edges e
                    JOIN dependents d ON e."FromConceptId" = d.id
                    WHERE e."Kind" = 1
                      AND NOT e."IsDeleted"
                )
                SELECT id AS "Value" FROM dependents
                """)
            .ToListAsync(ct);

        return await ConceptsByIdAsync(ids, ct);
    }

    /// A valid learning order for a whole domain: every concept appears after everything it
    /// requires. Kahn's algorithm, by depth.
    ///
    /// This is not *the* plan — the Compiler builds that from the Twin and the learner's
    /// constraints (M4). This is the order the graph alone permits.
    public async Task<IReadOnlyList<Concept>> TopologicalOrderAsync(
        string domain, CancellationToken ct = default)
    {
        var ordered = await _db.Database
            .SqlQuery<Guid>($"""
                WITH RECURSIVE depth(id, level) AS (
                    -- Roots: concepts in this domain that require nothing.
                    SELECT c."Id", 0
                    FROM graph.concepts c
                    WHERE c."Domain" = {domain}
                      AND NOT c."IsDeleted"
                      AND NOT EXISTS (
                          SELECT 1 FROM graph.concept_edges e
                          WHERE e."ToConceptId" = c."Id" AND e."Kind" = 1 AND NOT e."IsDeleted"
                      )

                    UNION ALL

                    SELECT e."ToConceptId", d.level + 1
                    FROM graph.concept_edges e
                    JOIN depth d ON e."FromConceptId" = d.id
                    WHERE e."Kind" = 1 AND NOT e."IsDeleted"
                )
                -- A concept reachable by several paths takes its DEEPEST level: it cannot be
                -- learned until its slowest prerequisite chain is done.
                SELECT d.id AS "Value"
                FROM depth d
                JOIN graph.concepts c ON c."Id" = d.id
                WHERE c."Domain" = {domain}
                GROUP BY d.id, c."Slug"
                ORDER BY MAX(d.level), c."Slug"
                """)
            .ToListAsync(ct);

        var concepts = await ConceptsByIdAsync(ordered, ct);
        var byId = concepts.ToDictionary(c => c.Id);

        return [.. ordered.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    /// **The learner's frontier**: what they are ready to learn *right now*.
    ///
    /// Every concept they do not hold, whose prerequisites they all hold. This is the honest
    /// edge of someone's knowledge — and, in the UI, the one dopamine PRAXIS permits itself: the
    /// frontier advancing. It is earned, it is real, and it is enough (R-3).
    ///
    /// Held concepts come from the Twin (M3). The Graph does not know what a learner knows and
    /// must never guess.
    public async Task<IReadOnlyList<Concept>> FrontierAsync(
        string domain,
        IReadOnlyCollection<Guid> heldConceptIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(heldConceptIds);

        var held = heldConceptIds.ToArray();

        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT c."Id" AS "Value"
                FROM graph.concepts c
                WHERE c."Domain" = {domain}
                  AND NOT c."IsDeleted"
                  AND c."IsActive"

                  -- Not already held.
                  AND NOT (c."Id" = ANY({held}))

                  -- And nothing it requires is still missing.
                  AND NOT EXISTS (
                      SELECT 1
                      FROM graph.concept_edges e
                      WHERE e."ToConceptId" = c."Id"
                        AND e."Kind" = 1
                        AND NOT e."IsDeleted"
                        AND NOT (e."FromConceptId" = ANY({held}))
                  )
                """)
            .ToListAsync(ct);

        return await ConceptsByIdAsync(ids, ct);
    }

    /// The concepts a learner cannot reach yet, and how many prerequisites still stand between
    /// them and each one. This is what turns "you are not ready" into "you are three concepts
    /// away", which is the difference between a wall and a ladder.
    public async Task<IReadOnlyList<BlockedConcept>> BlockedAsync(
        string domain,
        IReadOnlyCollection<Guid> heldConceptIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(heldConceptIds);

        var held = heldConceptIds.ToArray();

        return await _db.Database
            .SqlQuery<BlockedConcept>($"""
                SELECT c."Id" AS "ConceptId",
                       c."Slug" AS "Slug",
                       COUNT(e."Id")::int AS "MissingPrerequisites"
                FROM graph.concepts c
                JOIN graph.concept_edges e ON e."ToConceptId" = c."Id"
                WHERE c."Domain" = {domain}
                  AND NOT c."IsDeleted"
                  AND NOT (c."Id" = ANY({held}))
                  AND e."Kind" = 1
                  AND NOT e."IsDeleted"
                  AND NOT (e."FromConceptId" = ANY({held}))
                GROUP BY c."Id", c."Slug"
                ORDER BY COUNT(e."Id"), c."Slug"
                """)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<Concept>> ConceptsByIdAsync(
        List<Guid> ids, CancellationToken ct)
        => ids.Count == 0
            ? []
            : await _db.Concepts
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .OrderBy(c => c.Slug)
                .ToListAsync(ct);
}

public sealed record BlockedConcept(Guid ConceptId, string Slug, int MissingPrerequisites);
