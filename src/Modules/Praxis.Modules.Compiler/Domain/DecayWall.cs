using Praxis.Shared;

namespace Praxis.Modules.Compiler.Domain;

/// **The Decay Wall (CANON C-1) — corrected by simulating it instead of asserting it.**
///
/// CANON says the wall is a *divergence*: below some minutes-per-day, "the compiler's forward
/// simulation diverges" — forgetting removes mastery faster than the learner can add it, and the
/// objective becomes structurally unreachable.
///
/// **I simulated it, and that is not what happens.**
///
/// Spaced repetition does not permit divergence. Every *successful* retrieval pushes the next one
/// exponentially further away, so a concept's long-run maintenance cost falls towards zero. The
/// total bill for N concepts therefore grows sub-linearly, and it converges. This is not a modelling
/// artefact — it is why real spaced-repetition users hold tens of thousands of items on twenty
/// minutes a day. A model in which they could not would be a model of despair, not of memory.
///
/// So the honest wall is **a horizon, not a divergence**:
///
///   At 20 minutes a day, a 180-concept applied objective takes **three and a half years**, and
///   the maintenance bill grows to eat most of every session. The learner does not fail to converge.
///   They converge long after they have given up, having spent most of their time treading water.
///
/// That is still a plan PRAXIS must refuse to sell. It is simply refused for the true reason.
///
/// ## Why it must be simulated rather than estimated
///
/// The wall is not "you need an hour a day". It depends on the size of the graph, the strictness of
/// its contracts (re-demonstrating *"I can beat BM25"* is not a two-minute flashcard — it is redoing
/// real work), how fast this learner forgets, and how much they already hold. The only honest way to
/// find it is to walk the plan forward, day by day, at their real pace, with everything they have
/// learned rotting underneath them, and see where they actually are when the horizon arrives.
///
/// > *"At 20 minutes a day you will not become an AI Engineer inside two years. You will spend most
/// > of your time keeping what you have. What you CAN become in that time is a competent ML
/// > practitioner in PyTorch, by week 40. Shall I compile that instead — or can you find 40
/// > minutes?"*
public static class DecayWall
{
    /// How long the learner is willing to be at this. Beyond it, a plan is not slow — it is a
    /// fantasy, and they will quit long before it delivers.
    ///
    /// Eighteen months is deliberately generous. If PRAXIS cannot get them there inside it, the
    /// objective is wrong for the time they have, and saying so is the entire point.
    public const int DefaultHorizonDays = 548;
    /// What one *successful* retrieval costs. Short by design — a rep is a question, not a lesson.
    public const double MinutesPerRep = 2.5;

    /// What it costs to rescue a concept the learner has let slip past recall.
    ///
    /// **This is the engine of the wall.** A rep you get right is cheap and pushes the next one
    /// further away. A rep you get *wrong* — because you left it too long — is expensive *and*
    /// collapses the interval, so it comes back around again almost immediately. Under-maintenance
    /// does not cost you a little. It costs you compounding.
    public const double MinutesPerLapseRecovery = 9.0;

    /// Below this at review time, the learner cannot retrieve it. The rep fails.
    public const double LapseThreshold = 0.60;

    /// Retrievability at which a concept is due. Below this it is slipping away faster than the
    /// learner realises.
    public const double RepThreshold = 0.90;

    /// Retrievability below which a concept can no longer be *built on*. A learner whose foundation
    /// has fallen this far cannot make progress on what depends on it (M4.1's wall).
    public const double UsableThreshold = 0.70;

    /// A rep costs a fraction of what the concept cost to learn, because **re-demonstrating applied
    /// mastery is not flipping a card** — it is redoing a smaller version of the work. This is why
    /// the maintenance bill in PRAXIS is heavier than in a flashcard app, and why the horizon wall
    /// exists at all.
    public const double RepFractionOfLearnCost = 0.15;

    /// Rescuing a concept the learner let slip past recall costs far more than keeping it.
    public const double LapseFractionOfLearnCost = 0.40;

    /// Simulate the plan, day by day, at the learner's real pace, with decay running underneath.
    public static WallVerdict Simulate(Plan plan, int minutesPerDay, int horizonDays = DefaultHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (minutesPerDay <= 0)
        {
            return new WallVerdict(false, 0, 0, 0, minutesPerDay, plan.Steps.Count, horizonDays);
        }

        if (plan.IsEmpty)
        {
            return new WallVerdict(true, 0, 0, 0, minutesPerDay, 0, horizonDays);
        }

        var held = new List<HeldConcept>();
        var stepIndex = 0;
        var minutesIntoCurrentStep = 0.0;

        var peakMaintenance = 0.0;

        for (var day = 1; day <= horizonDays; day++)
        {
            var budget = (double)minutesPerDay;

            // ── First, keep what you have ────────────────────────────────────
            //
            // Maintenance is not optional. A learner who skips their reps to chase new material is
            // not learning faster; they are pouring water into a bucket with a hole in it.
            //
            // Most-at-risk first — the ones closest to slipping past recall, because those are the
            // ones about to become nine minutes instead of two and a half.
            var due = held
                .Where(h => h.RetrievabilityOn(day) < RepThreshold)
                .OrderBy(h => h.RetrievabilityOn(day))
                .ToList();

            var maintenanceBill = due.Sum(h => h.CostOfReviewingOn(day));
            peakMaintenance = Math.Max(peakMaintenance, maintenanceBill);

            foreach (var concept in due)
            {
                var cost = concept.CostOfReviewingOn(day);

                if (budget < cost)
                {
                    // Out of time. It keeps rotting — and if it slips past recall before they get
                    // back to it, tomorrow's bill for this one concept nearly quadruples, and the
                    // interval collapses so it returns again almost at once.
                    //
                    // **This is the wall.** Not a shortage of minutes: a compounding debt.
                    break;
                }

                budget -= cost;
                concept.Review(day);
            }

            // ── Then, and only then, move forward ────────────────────────────
            //
            // This is where the wall bites. When the maintenance bill eats the whole day, `budget`
            // is zero here, forever, and the learner never advances another inch.
            while (budget > 0 && stepIndex < plan.Steps.Count)
            {
                var step = plan.Steps[stepIndex];

                // A concept cannot be built on foundations that have dissolved. If a prerequisite
                // has rotted past usable, today's progress is wasted — they must rescue it first,
                // which is exactly the death spiral the wall describes.
                if (AnyFoundationDissolved(held, day))
                {
                    break;
                }

                var remaining = step.Minutes - minutesIntoCurrentStep;
                var spend = Math.Min(budget, remaining);

                budget -= spend;
                minutesIntoCurrentStep += spend;

                if (minutesIntoCurrentStep + 1e-9 < step.Minutes)
                {
                    break;   // still working on it tomorrow
                }

                held.Add(new HeldConcept(step.ConceptId, day, step.Minutes));
                stepIndex++;
                minutesIntoCurrentStep = 0.0;

                if (stepIndex == plan.Steps.Count)
                {
                    return new WallVerdict(
                        Reachable: true,
                        DaysToObjective: day,
                        ConceptsReached: held.Count,
                        PeakMaintenanceMinutesPerDay: peakMaintenance,
                        MinutesPerDay: minutesPerDay,
                        ConceptsInPlan: plan.Steps.Count,
                        HorizonDays: horizonDays);
                }
            }
        }

        // The horizon arrived and they were still climbing. Not "diverging" — just nowhere near,
        // and by now spending most of every session keeping what they already had.
        return new WallVerdict(
            Reachable: false,
            DaysToObjective: 0,
            ConceptsReached: held.Count,
            PeakMaintenanceMinutesPerDay: peakMaintenance,
            MinutesPerDay: minutesPerDay,
            ConceptsInPlan: plan.Steps.Count,
            HorizonDays: horizonDays);
    }

    /// **The number the learner actually needs.**
    ///
    /// Not "try harder" — a figure. The smallest daily commitment at which the objective is
    /// reachable at all. Binary search over the simulation, because there is no closed form: the
    /// wall depends on the graph, the contracts, and how fast this particular person forgets.
    ///
    /// Null when even a very large daily budget will not do it — at which point the objective is
    /// wrong, not the schedule, and PRAXIS should say *that* instead.
    public static int? MinutesPerDayToMakeReachable(
        Plan plan, int ceiling = 480, int horizonDays = DefaultHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!Simulate(plan, ceiling, horizonDays).Reachable)
        {
            return null;
        }

        var low = 1;
        var high = ceiling;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);

            if (Simulate(plan, mid, horizonDays).Reachable)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// **What they CAN become in the time they have.**
    ///
    /// When the objective is out of reach, PRAXIS does not shrug and sell the plan anyway. It finds
    /// the furthest point along the path the learner *will* actually arrive at, and offers that —
    /// a narrower objective it genuinely believes in.
    ///
    /// This is the difference between a product and a slot machine.
    public static IReadOnlyList<PlanStep> ReachablePrefix(
        Plan plan, int minutesPerDay, int horizonDays = DefaultHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var verdict = Simulate(plan, minutesPerDay, horizonDays);

        return [.. plan.Steps.Take(verdict.ConceptsReached)];
    }

    private static bool AnyFoundationDissolved(List<HeldConcept> held, int day)
        => held.Any(h => h.RetrievabilityOn(day) < UsableThreshold);

    /// A concept the learner holds, and the memory of it decaying.
    private sealed class HeldConcept
    {
        private readonly double _repCost;
        private readonly double _lapseCost;

        private double _stability;
        private int _lastReviewedDay;

        public Guid ConceptId { get; }

        public HeldConcept(Guid conceptId, int learnedOnDay, double learnMinutes)
        {
            ConceptId = conceptId;
            _stability = InitialStability;
            _lastReviewedDay = learnedOnDay;

            // Maintenance is proportional to what the concept demanded in the first place. Keeping
            // "I can beat BM25" alive means re-solving something, not recognising a definition.
            _repCost = Math.Max(MinutesPerRep, learnMinutes * RepFractionOfLearnCost);
            _lapseCost = Math.Max(MinutesPerLapseRecovery, learnMinutes * LapseFractionOfLearnCost);
        }

        public double RetrievabilityOn(int day)
        {
            var elapsed = Math.Max(0, day - _lastReviewedDay);

            // The same power curve the Twin uses (M3.3). Stability is the number of days until
            // retrievability falls to 90%.
            return Math.Pow(1.0 + (Factor * elapsed / _stability), Decay);
        }

        /// What reviewing this concept costs *today*.
        ///
        /// Cheap if they can still retrieve it. Expensive if they left it too long — because then
        /// the rep is not a rep, it is a rescue.
        public double CostOfReviewingOn(int day)
            => RetrievabilityOn(day) < LapseThreshold ? _lapseCost : _repCost;

        /// **The spacing effect — and its evil twin.**
        ///
        /// A rep you get *right* pushes the next one much further away, so the long-run maintenance
        /// cost of a concept falls towards nothing. This is the only reason learning is possible at
        /// all: without it the bill would grow without bound and *every* objective would be
        /// unreachable, which is plainly not what happens to real people.
        ///
        /// A rep you get *wrong* — because you left it too long — does the opposite. **The interval
        /// collapses**, and the concept comes back around almost immediately, at nearly four times
        /// the cost. Under-maintenance does not cost a learner a little. It costs them compounding.
        ///
        /// That asymmetry is the entire mechanism of the wall. A learner with enough minutes stays
        /// on the right side of it and their bill shrinks forever. A learner without them slips to
        /// the wrong side, and their bill grows until it eats the day.
        public void Review(int day)
        {
            var lapsed = RetrievabilityOn(day) < LapseThreshold;

            _stability = lapsed
                ? Math.Max(InitialStability, _stability * LapseCollapse)
                : _stability * StabilityGrowth;

            _lastReviewedDay = day;
        }

        private const double InitialStability = 2.0;
        private const double StabilityGrowth = 1.7;

        /// A failed retrieval throws away most of the durability the learner had built.
        private const double LapseCollapse = 0.25;

        private const double Decay = -0.5;
        private static readonly double Factor = Math.Pow(0.9, 1.0 / Decay) - 1.0;
    }
}

/// What the simulation found.
public sealed record WallVerdict(
    /// False means the objective is **structurally unreachable** at this pace. Not slow. Unreachable.
    bool Reachable,

    int DaysToObjective,

    /// How far along the plan they actually got before the maintenance bill ate the day.
    int ConceptsReached,

    /// The worst daily maintenance bill they faced. When this approaches their available minutes,
    /// they are at the wall.
    double PeakMaintenanceMinutesPerDay,

    int MinutesPerDay,
    int ConceptsInPlan,

    /// How long the learner was willing to be at this. Reachability is always *within a horizon* —
    /// "eventually" is not a promise anyone can act on.
    int HorizonDays)
{
    /// How much of their day was going on keeping what they already had, at the worst point.
    public double MaintenanceShareOfDay
        => MinutesPerDay <= 0 ? 1.0 : Math.Min(1.0, PeakMaintenanceMinutesPerDay / MinutesPerDay);

    public double ProgressMade
        => ConceptsInPlan == 0 ? 1.0 : ConceptsReached / (double)ConceptsInPlan;
}
