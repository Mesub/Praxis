using Praxis.Shared;

namespace Praxis.Modules.Compiler.Domain;

/// What a concept is *worth* to this learner, in minutes, right now.
///
/// **`make` for the mind.** The graph is the dependency DAG; what the learner already holds is the
/// cache; forgetting is cache invalidation. This is the cost of rebuilding one target — and, like
/// any build system, the answer depends on what is already in the cache and how stale it is.
///
/// ## What goes into it, and why
///
/// - **The concept's estimated minutes.** The pack's prior, which cohort data will eventually
///   correct.
///
/// - **What the learner already holds.** A concept they are 80% of the way to is not a fresh
///   concept. But it is *not* 80% cheaper either — the last stretch of any concept is the expensive
///   part, because that is where the never-seen problem and the build that runs live, and where the
///   Evidence Ceiling stops them getting there by talking. So the discount is deliberately sublinear.
///
/// - **The strictness of its proof contract.** A concept that demands two never-seen problems and a
///   build that passes hidden tests costs more than one satisfied by a recall item. The contract is
///   not decoration; it is a large part of the work.
///
/// - **Decay pressure on its prerequisites.** This is the part no other roadmap tool has. If the
///   things this concept depends on are rotting, the learner will not merely find it hard — they
///   will find it *impossible*, and will not know why. That is the wall, and the Compiler must
///   price it rather than walk them into it.
///
/// Pure. No clock, no randomness, no model. The plan is a solver's output, not a prompt's (doc 24
/// §3.4) — the compiler is Dijkstra, and this is its edge weight.
public static class ConceptCost
{
    /// Minutes of real work for this learner to take this concept from where they are to held.
    public static double Minutes(ConceptCostInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var baseMinutes = Math.Max(1.0, inputs.EstimatedMinutes);

        // ── What is already in the cache ─────────────────────────────────────
        //
        // Sublinear on purpose. Being 80% of the way to a concept does not make the remainder 80%
        // cheaper: the last stretch is where the never-seen problem and the build that runs are,
        // and the Evidence Ceiling means it cannot be crossed by explaining more. A learner who has
        // read everything and produced nothing has done the cheap part.
        var held = Math.Clamp(inputs.MasteryCredit, 0.0, 1.0);
        var remaining = Math.Pow(1.0 - held, HeldExponent);

        // ── How strict the standard of proof is ──────────────────────────────
        var strictness = Strictness(inputs.Contract);

        // ── The wall ─────────────────────────────────────────────────────────
        //
        // Prerequisites that are rotting make a concept disproportionately expensive: the learner
        // is trying to build on foundations that are dissolving under them. Multiplicative, because
        // it does not add work — it *undermines* it.
        var decayPressure = DecayPressure(inputs.PrerequisiteRetrievability);

        // ── Their own difficulty with it ─────────────────────────────────────
        //
        // Not the concept's difficulty prior — the graph's opinion. This is how hard *this person*
        // has found it, revealed by what they actually did (FSRS difficulty, 1–10).
        var personal = 0.7 + (Math.Clamp(inputs.LearnerDifficulty, 1.0, 10.0) - 1.0) * 0.0667;

        return baseMinutes * remaining * strictness * decayPressure * personal;
    }

    /// How much work the contract itself demands, as a multiplier on the concept's base minutes.
    ///
    /// A concept satisfied by recalling a definition is cheap. One that demands two never-seen
    /// problems *and* code that passes hidden tests is not — and pricing them the same is how a
    /// roadmap ends up promising twelve weeks and taking thirty.
    public static double Strictness(MasteryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        // Only the unfakeable clauses drive the cost. Explaining is cheap and, by the Floor, never
        // sufficient — so it cannot be the thing that makes a concept expensive.
        var required = contract.Clauses
            .Where(c => !c.Type.IsModelGraded())
            .Sum(c => WeightOf(c.Type) * c.RequiredCount);

        // A high hold threshold means more of the work has to actually land.
        var bar = 0.6 + (contract.HoldThreshold * 0.6);

        return Math.Max(MinStrictness, required * bar);
    }

    /// What each kind of proof costs to produce, relative to a single applied problem.
    ///
    /// A build that runs against hidden tests is the most expensive thing a learner can be asked
    /// for, and it should be — it is also the least fakeable.
    private static double WeightOf(EvidenceType type) => type switch
    {
        EvidenceType.Apply => 1.0,
        EvidenceType.Build => 2.2,
        EvidenceType.Predict => 1.4,
        _ => 0.0,   // model-graded: cheap to produce, and never sufficient
    };

    /// **The cost of building on foundations that are dissolving.**
    ///
    /// 1.0 when every prerequisite is fresh. It climbs steeply as they rot — because a learner
    /// attempting a concept whose prerequisites they have half-forgotten does not experience it as
    /// "harder". They experience it as *impossible*, and they blame themselves.
    ///
    /// This is what the Compiler prices so that the Scheduler can refuse to walk them into it.
    public static double DecayPressure(IReadOnlyList<double> prerequisiteRetrievability)
    {
        ArgumentNullException.ThrowIfNull(prerequisiteRetrievability);

        if (prerequisiteRetrievability.Count == 0)
        {
            return 1.0;   // a root concept depends on nothing, so nothing can be rotting under it
        }

        // The *weakest* prerequisite governs, not the average. One dissolved foundation is enough:
        // a learner who has forgotten embeddings cannot do vector retrieval, however fresh their
        // tokenisation is. Averaging would let strong prerequisites hide a fatal one.
        var weakest = prerequisiteRetrievability.Min();
        var rot = 1.0 - Math.Clamp(weakest, 0.0, 1.0);

        return 1.0 + (MaxDecayPenalty * rot * rot);
    }

    /// Being 80% of the way to a concept makes it ~35% cheaper, not 80% cheaper.
    private const double HeldExponent = 0.55;

    /// Even the gentlest contract is some work.
    private const double MinStrictness = 0.35;

    /// A concept whose weakest prerequisite is entirely gone costs four times what it would have.
    private const double MaxDecayPenalty = 3.0;
}

/// Everything the cost function needs, as plain values.
///
/// The Compiler does not know the Graph or the Twin exist. It is handed a snapshot and returns a
/// number — which is what lets it be a solver rather than a service, and what makes it testable
/// without a database.
public sealed record ConceptCostInputs(
    Guid ConceptId,

    /// The pack's prior for a median learner. Corrected by cohort data, eventually.
    int EstimatedMinutes,

    /// What the Twin says they have already proved. In [0, 1].
    double MasteryCredit,

    /// How hard *this learner* finds it, on the FSRS scale (1–10). Not the graph's opinion.
    double LearnerDifficulty,

    /// What it would take to prove it.
    MasteryContract Contract,

    /// How available each direct prerequisite is *right now*, on the forgetting curve. Empty for a
    /// root concept.
    IReadOnlyList<double> PrerequisiteRetrievability);
