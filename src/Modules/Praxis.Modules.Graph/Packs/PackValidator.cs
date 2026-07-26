using Praxis.Modules.Graph.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Packs;

/// The gate a domain pack must pass before it is allowed anywhere near a learner.
///
/// **Nothing here throws on the first problem.** It collects every one, because an author fixing
/// a 180-concept graph one error per run is an author who stops caring about the graph.
///
/// What it refuses, and why each one matters more than it looks:
///
/// - **A cycle in REQUIRES.** A depends on B depends on A. The Compiler would never terminate, or
///   would silently drop an edge to make progress. Neither is acceptable — the first hangs, the
///   second lies.
/// - **A concept with no PoM contract.** Then nothing could ever prove it, and the learner works
///   forever on something they can never hold. If you cannot say what would prove it, it is a
///   topic, and topics do not belong in a graph.
/// - **A contract that violates the Evidence Ceiling.** Caught by `PoMContract.Create` (C-4), and
///   surfaced here with the concept's name so the author knows *which* one is dishonest.
/// - **An orphan.** A concept connected to nothing. Either an author forgot its prerequisites —
///   in which case a learner will be walked into a wall — or it does not belong in this pack.
/// - **A dangling reference.** An edge to a slug that does not exist. Usually a typo, and a typo
///   in a prerequisite is indistinguishable from a missing prerequisite until someone fails.
public static class PackValidator
{
    /// Matches Assessment's learner-model columns (BankedItem.TargetsMisconception,
    /// LearnerMisconception.Key), both `HasMaxLength(64)`. A key the pack loader accepts but those
    /// columns cannot store would cross the wire, survive the generator's airlock, and then blow
    /// up `SaveChangesAsync` in `EnsureConceptPool` — losing the whole generated batch, silently.
    private const int MaxMisconceptionKeyLength = 64;

    public static Result<ValidatedPack> Validate(PackDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(document.Domain))
        {
            errors.Add("The pack has no domain.");
        }

        if (document.Version <= 0)
        {
            errors.Add("The pack has no version. Versions are how an old credential stays meaningful.");
        }

        if (document.Concepts.Count == 0)
        {
            errors.Add("The pack has no concepts.");
            return Result<ValidatedPack>.Invalid("This pack cannot be loaded.", errors);
        }

        var bySlug = new Dictionary<string, PackConcept>(StringComparer.OrdinalIgnoreCase);

        foreach (var concept in document.Concepts)
        {
            if (string.IsNullOrWhiteSpace(concept.Slug))
            {
                errors.Add($"A concept titled '{concept.Title}' has no slug.");
                continue;
            }

            var slug = concept.Slug.Trim().ToLowerInvariant();

            if (!bySlug.TryAdd(slug, concept))
            {
                errors.Add(
                    $"'{slug}' is defined twice. The slug is how the item bank, the graph and a "
                    + "piece of Evidence all name the same idea — two concepts sharing one would "
                    + "silently merge three different things.");
            }
        }

        foreach (var concept in document.Concepts.Where(c => !string.IsNullOrWhiteSpace(c.Slug)))
        {
            ValidateConcept(concept, bySlug, errors);
        }

        ValidateMisconceptionKeysAreGloballyUnique(document, errors);

        DetectCycles(bySlug, errors);
        DetectOrphans(bySlug, errors);

        if (errors.Count > 0)
        {
            return Result<ValidatedPack>.Invalid(
                $"This pack cannot be loaded: {errors.Count} problem(s).", errors);
        }

        return Result<ValidatedPack>.Success(new ValidatedPack(document, bySlug));
    }

    private static void ValidateConcept(
        PackConcept concept,
        Dictionary<string, PackConcept> bySlug,
        List<string> errors)
    {
        var slug = concept.Slug.Trim().ToLowerInvariant();

        foreach (var (kind, targets) in EdgesOf(concept))
        {
            foreach (var target in targets)
            {
                var to = target.Trim().ToLowerInvariant();

                if (!bySlug.ContainsKey(to))
                {
                    errors.Add(
                        $"'{slug}' declares {kind} on '{to}', which does not exist in this pack. "
                        + "A typo in a prerequisite is indistinguishable from a missing "
                        + "prerequisite until a learner fails.");
                }

                if (string.Equals(to, slug, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"'{slug}' declares {kind} on itself.");
                }
            }
        }

        if (concept.Proof is null)
        {
            errors.Add(
                $"'{slug}' has no proof contract. Nothing could ever demonstrate it, so a learner "
                + "would work on it forever without ever holding it. If you cannot say what would "
                + "prove it, it is a topic, not a concept.");

            return;
        }

        if (concept.Proof.Evidence.Count == 0)
        {
            errors.Add($"'{slug}' has a proof contract that accepts no evidence.");
            return;
        }

        foreach (var clause in concept.Proof.Evidence)
        {
            if (clause.Format is not ItemFormat.Prose && clause.Type is not EvidenceType.Apply)
            {
                errors.Add(
                    $"'{slug}' declares format '{clause.Format}' on {clause.Type} evidence. A "
                    + "format is a claim about how an item is *marked*, and only apply evidence is "
                    + "marked against a fixed key. Explain and critique are graded by a model "
                    + "against a rubric; there is nothing for them to choose between.");
            }

            if (clause.Format is ItemFormat.MultipleChoice && concept.Misconceptions.Count == 0)
            {
                errors.Add(
                    $"'{slug}' declares multiple-choice apply evidence but no misconceptions. A "
                    + "distractor exists to catch a *named* wrong belief — without one, three of "
                    + "the four options are noise, a wrong answer diagnoses nothing, and the item "
                    + "is a coin toss wearing a question mark.");
            }
        }

        // Build the real contract — that is what enforces the Evidence Ceiling (C-4). We surface
        // its complaint with the concept's name attached, so the author knows which one is
        // dishonest rather than merely that something, somewhere, is.
        try
        {
            _ = PoMContract.Create(
                Guid.CreateVersion7(),
                concept.Proof.Hold,
                [.. concept.Proof.Evidence.Select(e =>
                    AcceptedEvidence.Of(e.Type, e.MinScore, e.Max, e.Count, e.Format))]);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"'{slug}': {ex.Message.Split(" (Parameter")[0]}");
        }

        ValidateMisconceptions(concept, slug, errors);
    }

    /// A declared misconception is a closed target the AI extractor matches against — so each one
    /// must actually be usable as a key and recognisable as a belief.
    private static void ValidateMisconceptions(PackConcept concept, string slug, List<string> errors)
    {
        foreach (var m in concept.Misconceptions)
        {
            if (string.IsNullOrWhiteSpace(m.Key))
            {
                errors.Add(
                    $"'{slug}' declares a misconception with no key. The key is how the same wrong "
                    + "idea in two learners is recognised as one — without it, it cannot be mined.");
                continue;
            }

            if (!IsWellFormedKey(m.Key))
            {
                errors.Add(
                    $"'{slug}' declares misconception key '{m.Key}', which is not kebab-case "
                    + "(lowercase letters, digits and hyphens). Keys are identifiers, not prose.");
            }

            if (m.Key.Trim().Length > MaxMisconceptionKeyLength)
            {
                errors.Add(
                    $"'{slug}' declares misconception key '{m.Key}', which is "
                    + $"{m.Key.Trim().Length} characters long. Keys cannot exceed "
                    + $"{MaxMisconceptionKeyLength} characters — the learner-model columns that "
                    + "store them (BankedItem.TargetsMisconception, LearnerMisconception.Key) are "
                    + $"sized to that limit.");
            }

            if (string.IsNullOrWhiteSpace(m.Statement))
            {
                errors.Add(
                    $"'{slug}' misconception '{m.Key}' has no statement. A misconception a learner "
                    + "cannot read back in their own voice is one they will never recognise as theirs.");
            }
        }
    }

    /// Misconception keys are **global** — the same wrong idea, wherever it is declared, is one idea.
    /// Two concepts declaring the same key would split one belief in two, or claim two beliefs are
    /// one; either way the cohort-scale mining that keys exist for quietly breaks.
    private static void ValidateMisconceptionKeysAreGloballyUnique(
        PackDocument document, List<string> errors)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var concept in document.Concepts)
        {
            foreach (var m in concept.Misconceptions.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                var key = m.Key.Trim().ToLowerInvariant();

                if (seen.TryGetValue(key, out var firstOwner))
                {
                    errors.Add(
                        $"Misconception key '{key}' is declared on both '{firstOwner}' and "
                        + $"'{concept.Slug}'. A key names one wrong idea across the whole pack — "
                        + "declare it once, on the concept it most belongs to.");
                }
                else
                {
                    seen[key] = concept.Slug;
                }
            }
        }
    }

    private static bool IsWellFormedKey(string key)
    {
        foreach (var c in key)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                return false;
            }
        }

        return key.Length > 0 && key[0] != '-' && key[^1] != '-';
    }

    /// Depth-first, with an explicit stack, so a deep graph cannot blow the real one.
    private static void DetectCycles(Dictionary<string, PackConcept> bySlug, List<string> errors)
    {
        var state = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var slug in bySlug.Keys)
        {
            if (state.GetValueOrDefault(slug) == VisitState.Done)
            {
                continue;
            }

            Walk(slug);
        }

        void Walk(string slug)
        {
            state[slug] = VisitState.Visiting;
            path.Add(slug);

            foreach (var requirement in Requires(slug))
            {
                if (!bySlug.ContainsKey(requirement))
                {
                    continue;   // already reported as dangling
                }

                switch (state.GetValueOrDefault(requirement))
                {
                    case VisitState.Visiting:
                        var from = path.IndexOf(requirement);
                        var cycle = path.Skip(from).Append(requirement);

                        errors.Add(
                            $"REQUIRES cycle: {string.Join(" -> ", cycle)}. The Compiler would "
                            + "either never terminate or silently drop an edge to make progress. "
                            + "The first hangs; the second lies.");
                        break;

                    case VisitState.Unseen:
                        Walk(requirement);
                        break;

                    default:
                        break;
                }
            }

            path.RemoveAt(path.Count - 1);
            state[slug] = VisitState.Done;
        }

        IEnumerable<string> Requires(string slug)
            => bySlug.TryGetValue(slug, out var c)
                ? c.Requires.Select(r => r.Trim().ToLowerInvariant())
                : [];
    }

    private static void DetectOrphans(Dictionary<string, PackConcept> bySlug, List<string> errors)
    {
        if (bySlug.Count < 2)
        {
            return;   // a one-concept pack cannot have an orphan
        }

        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (slug, concept) in bySlug)
        {
            foreach (var (_, targets) in EdgesOf(concept))
            {
                foreach (var target in targets.Select(t => t.Trim().ToLowerInvariant()))
                {
                    if (!bySlug.ContainsKey(target))
                    {
                        continue;
                    }

                    connected.Add(slug);
                    connected.Add(target);
                }
            }
        }

        foreach (var slug in bySlug.Keys.Where(s => !connected.Contains(s)))
        {
            errors.Add(
                $"'{slug}' is connected to nothing. Either its prerequisites were forgotten — and "
                + "a learner will be walked into a wall — or it does not belong in this pack.");
        }
    }

    private static IEnumerable<(EdgeKind Kind, List<string> Targets)> EdgesOf(PackConcept concept)
    {
        yield return (EdgeKind.Requires, concept.Requires);
        yield return (EdgeKind.Contrasts, concept.Contrasts);
        yield return (EdgeKind.Relates, concept.Relates);
        yield return (EdgeKind.AppliesTo, concept.AppliesTo);
    }

    private enum VisitState
    {
        Unseen = 0,
        Visiting = 1,
        Done = 2,
    }
}

/// A pack that has been through the gate. Only the loader can act on one of these, so it is not
/// possible to persist a graph that was never validated.
public sealed class ValidatedPack
{
    public PackDocument Document { get; }
    public IReadOnlyDictionary<string, PackConcept> BySlug { get; }

    internal ValidatedPack(PackDocument document, IReadOnlyDictionary<string, PackConcept> bySlug)
        => (Document, BySlug) = (document, bySlug);
}
