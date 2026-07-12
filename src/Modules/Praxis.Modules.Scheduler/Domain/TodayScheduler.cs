using Praxis.Modules.Compiler.Domain;

namespace Praxis.Modules.Scheduler.Domain;

/// **What do I do in the next fifteen minutes?**
///
/// The Scheduler merges three things that are all shouting at once — knowledge that is rotting, reps
/// that are due, and the next concept in the plan — and picks **one**.
///
/// ## The priority order, and why it is not obvious
///
/// **1. Rescue what is about to be lost.**
///
/// A concept that has decayed below usable is not merely "due". It is *load-bearing*: everything
/// above it in the graph now costs multiples more (M4.1), and the learner attempting that work will
/// not experience it as harder — they will experience it as *impossible*, and they will blame
/// themselves. Nine minutes rescuing a foundation buys back hours of work they were about to waste.
///
/// This is the whole reason PRAXIS models decay at all, and it is the number one job.
///
/// **2. Then learn something new — even when reps are still outstanding.**
///
/// This is the counterintuitive one, and it took some thought.
///
/// The naive scheduler clears the rep queue first and learns with what is left. It feels
/// responsible. It is a trap: on a busy day the queue *is* the session, and the learner spends
/// weeks revising and never advances. They are working hard, the graph is not moving, and they
/// eventually conclude — correctly — that this is going nowhere.
///
/// So reps are **capped**. A session always leaves room to move forward, because a learner who never
/// advances does not stay a learner. The exception is rescue: a foundation actually dissolving beats
/// everything, because building on it is worse than not building at all.
///
/// **3. And when there is genuinely nothing to do, say so.**
///
/// Not "here are some optional extras". Not a generated quiz to keep the streak alive. PRAXIS
/// optimises for the learner *leaving* (I-10). An empty day is a real state and it gets said out
/// loud.
///
/// Pure. Given the same learner and the same minutes, the same card — because a scheduler that says
/// something different each time you look at it is not scheduling, it is stalling.
public static class TodayScheduler
{
    /// The most of a session that may go on maintenance before the learner is simply revising.
    ///
    /// Above this, they are working hard and going nowhere — and they will notice, and they will be
    /// right.
    public const double MaxRepShareOfSession = 0.60;

    /// Below this retrievability, a concept is not "due" — it is **going**. Everything built on it is
    /// now more expensive, and the learner does not know why.
    public const double RescueThreshold = 0.70;

    public static TodayCard Choose(SchedulerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var deferred = new List<Deferred>();

        // ── 1. Is anything actually dissolving? ──────────────────────────────
        //
        // Ordered by how far gone it is — the most nearly lost first, because that is the one whose
        // rescue is about to get four times more expensive.
        var dissolving = inputs.Due
            .Where(d => d.Retrievability < RescueThreshold)
            .OrderBy(d => d.Retrievability)
            .ToList();

        if (dissolving.Count > 0)
        {
            var worst = dissolving[0];

            deferred.AddRange(Everything(inputs, except: worst.ConceptId,
                "Rescuing something that was about to be lost took priority."));

            return new TodayCard(
                Kind: CardKind.Rescue,
                ConceptId: worst.ConceptId,
                Slug: worst.Slug,
                Headline: $"Rescue {worst.Slug} — you are about to lose it.",
                Because: $"You could retrieve this {worst.Retrievability:P0} of the time today. "
                         + "Everything you have built on top of it is getting harder while it slips, "
                         + "and you would not have known why.",
                Minutes: Math.Min(inputs.SessionMinutes, worst.Minutes),
                AlsoDue: deferred);
        }

        // ── 2. Reps, but never the whole session ─────────────────────────────
        var repBudget = (int)Math.Floor(inputs.SessionMinutes * MaxRepShareOfSession);

        var dueNow = inputs.Due
            .OrderBy(d => d.Retrievability)
            .ToList();

        // Would reps alone eat the session? Then take one and get on with it — the learner needs to
        // move forward today, not just tread water.
        if (dueNow.Count > 0 && dueNow[0].Minutes <= repBudget)
        {
            var rep = dueNow[0];

            deferred.AddRange(Everything(inputs, except: rep.ConceptId,
                "A rep was due, and reps are cheap."));

            return new TodayCard(
                Kind: CardKind.Rep,
                ConceptId: rep.ConceptId,
                Slug: rep.Slug,
                Headline: $"One question on {rep.Slug}.",
                Because: $"It has been long enough that you would get it right about "
                         + $"{rep.Retrievability:P0} of the time — which is exactly when recalling it "
                         + "is worth the most.",
                Minutes: rep.Minutes,
                AlsoDue: deferred);
        }

        // ── 3. Move forward ──────────────────────────────────────────────────
        if (inputs.NextStep is { } step)
        {
            deferred.AddRange(inputs.Due.Select(d => new Deferred(
                d.ConceptId, d.Slug,
                "Still holding. A rep can wait a day; the plan cannot wait forever.",
                d.Minutes)));

            var kind = step.Kind == StepKind.Refresh ? CardKind.Refresh : CardKind.Learn;

            return new TodayCard(
                Kind: kind,
                ConceptId: step.ConceptId,
                Slug: step.Slug,
                Headline: kind == CardKind.Refresh
                    ? $"Rebuild {step.Slug}. You held this once."
                    : $"{step.Slug}.",
                Because: kind == CardKind.Refresh
                    ? "You proved this before and have let it go. It is cheaper the second time — "
                      + "but it is not free, and the plan needs it back."
                    : $"It is the next thing your objective actually requires, and nothing is in "
                      + $"the way of it. Step {step.Position} of your plan.",
                Minutes: Math.Min(inputs.SessionMinutes, (int)Math.Ceiling(step.Minutes)),
                AlsoDue: deferred);
        }

        // ── 4. Nothing. Say so ───────────────────────────────────────────────
        //
        // No invented quiz. No streak to protect. PRAXIS optimises for the learner leaving.
        return new TodayCard(
            Kind: CardKind.Nothing,
            ConceptId: Guid.Empty,
            Slug: string.Empty,
            Headline: "Nothing is due, and your objective is reached.",
            Because: "There is no work worth doing today. PRAXIS will not invent some to keep you "
                     + "here — the point was never the app.",
            Minutes: 0,
            AlsoDue: []);
    }

    private static IEnumerable<Deferred> Everything(SchedulerInputs inputs, Guid except, string reason)
    {
        foreach (var due in inputs.Due.Where(d => d.ConceptId != except))
        {
            yield return new Deferred(due.ConceptId, due.Slug, reason, due.Minutes);
        }

        if (inputs.NextStep is { } step && step.ConceptId != except)
        {
            yield return new Deferred(
                step.ConceptId,
                step.Slug,
                "New work waits until the ground under it is solid.",
                (int)Math.Ceiling(step.Minutes));
        }
    }
}

/// Everything the Scheduler needs, as plain values. It does not know the Twin or the Compiler exist.
public sealed record SchedulerInputs(
    /// 5, 10, 15 or 25 — the scheduling atom. What the learner actually has.
    int SessionMinutes,

    /// Concepts whose retrievability has fallen below the review threshold.
    IReadOnlyList<DueConcept> Due,

    /// The next thing in the compiled plan, or null if the objective is reached.
    PlanStep? NextStep);

public sealed record DueConcept(
    Guid ConceptId,
    string Slug,

    /// How much of it they could retrieve *right now*. The lower it is, the more urgent — and the
    /// more valuable the rep, because retrieving something you nearly lost is worth far more than
    /// retrieving something fresh (M3.3).
    double Retrievability,

    int Minutes);
