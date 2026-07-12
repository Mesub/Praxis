using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **It is not trying to score you. It is trying to find where you break.**
///
/// A standard adaptive test maximises **information about θ** — it wants your ability to three
/// decimal places, and it will happily spend forty items getting there.
///
/// PRAXIS wants two other things instead, and they are both cheaper:
///
/// 1. **The decision, not the number.** *Does this learner hold this concept, at the contract
///    threshold?* We do not need θ to three decimals. We need the answer, in as few items as
///    possible — because every item saved is a minute given back to a human being (I-10).
///
/// 2. **The boundary, not the comfortable middle** (I-6). The engine will deliberately choose a
///    *slightly less informative* item about θ if that item can **resolve an open misconception**.
///    It is hunting for the place where the learner's model breaks, and it is willing to pay for the
///    privilege.
///
/// > The `λ_m` term below is Invention I-6, expressed as one line of arithmetic.
///
/// ## And the thing that stops it being cruel
///
/// This engine hunts failure, which means it is *always* asking you things you might get wrong. That
/// is right when it is working — the edge of what you can do is where learning happens. It is
/// **badly** wrong when someone is drowning, and an engine optimising purely for information will
/// cheerfully hand a struggling learner harder and harder items, **because a wrong answer is
/// informative.**
///
/// So the selector does not get the last word. `Adaptation` does — and it will stop the whole thing.
public static class ItemSelection
{
    // The weights from CANON §4.2. They are defaults, and they are *meant* to be argued with.
    public const double DecisionWeight = 1.00;

    /// **I-6, as a number.** The willingness to give up information about θ in exchange for finding
    /// out where somebody breaks.
    public const double MisconceptionWeight = 0.60;

    public const double ExposureWeight = 0.15;
    public const double CostWeight = 0.10;
    public const double FatigueWeight = 0.20;

    /// Pick the next item. **Highest utility wins** — and utility is not the same as informativeness.
    public static Result<Candidate> Choose(
        IReadOnlyList<Candidate> candidates,
        SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        if (candidates.Count == 0)
        {
            return Result<Candidate>.NotFound(
                "There are no items to choose from. PRAXIS will not invent one to keep the session "
                + "moving — a made-up item produces made-up evidence.");
        }

        var best = candidates
            .Select(c => (Item: c, Utility: Utility(c, context)))
            .OrderByDescending(x => x.Utility)
            .ThenBy(x => x.Item.Id, StringComparer.Ordinal)   // deterministic: same learner, same item
            .First();

        return Result<Candidate>.Success(best.Item);
    }

    /// The utility of asking this, of this person, right now.
    public static double Utility(Candidate item, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        var information = Irt.Information(context.Ability.Theta, item.A, item.B, item.C);

        return information
             + (DecisionWeight * DecisionInformation(item, context))
             + (MisconceptionWeight * MisconceptionInformation(item, context))
             - (ExposureWeight * item.Exposure)
             - (CostWeight * item.CostMinutes)
             - (FatigueWeight * context.Fatigue);
    }

    /// **What we are actually buying: certainty about the *decision*.**
    ///
    /// A one-step Bayesian lookahead. What would the Twin believe if they got this right? What if
    /// they got it wrong? How much of the *"do they hold it?"* uncertainty does each outcome remove?
    ///
    /// This is why a merely-hard item is not automatically a good item. An item far above the
    /// contract threshold sharpens θ beautifully and tells you **nothing about the decision** — you
    /// already knew they would probably fail it. The learner's afternoon paid for a number nobody
    /// needed.
    public static double DecisionInformation(Candidate item, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        var before = Entropy(HoldProbability(context.Ability, context.HoldThreshold));

        var correct = Irt.Update(context.Ability, [new Response(true, item.A, item.B, item.C)]);
        var wrong = Irt.Update(context.Ability, [new Response(false, item.A, item.B, item.C)]);

        var p = Irt.Probability(context.Ability.Theta, item.A, item.B, item.C);

        var after =
            (p * Entropy(HoldProbability(correct, context.HoldThreshold)))
            + ((1.0 - p) * Entropy(HoldProbability(wrong, context.HoldThreshold)));

        return Math.Max(0.0, before - after);
    }

    /// **The hunt** (I-6).
    ///
    /// An item that can *diagnose* an open misconception is worth extra — and it is worth **most**
    /// when PRAXIS is genuinely unsure whether the learner holds it, because that is the question an
    /// answer would actually settle.
    ///
    /// A misconception PRAXIS is already sure about — either way — is worth nothing to probe.
    /// Certainty is not a thing you buy twice.
    public static double MisconceptionInformation(Candidate item, SelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        return context.OpenMisconceptions
            .Where(m => item.Diagnoses.Contains(m.Key))
            .Sum(m => Entropy(m.Value));
    }

    /// P(the learner holds this concept), given what the Twin believes about their ability.
    ///
    /// A decision, expressed as the only honest thing it can be: **a probability, not a verdict.**
    public static double HoldProbability(Ability ability, double threshold)
    {
        ArgumentNullException.ThrowIfNull(ability);

        var z = (ability.Theta - threshold) / Math.Max(ability.StandardError, 1e-6);

        return NormalCdf(z);
    }

    /// Binary entropy, in bits. **1.0 is a coin flip — total ignorance. 0.0 is certainty.**
    public static double Entropy(double p)
    {
        if (p is <= 0.0 or >= 1.0)
        {
            return 0.0;
        }

        return -((p * Math.Log2(p)) + ((1.0 - p) * Math.Log2(1.0 - p)));
    }

    /// Abramowitz &amp; Stegun 7.1.26 — accurate to ~1e-7, which is several decimal places more than
    /// any decision here deserves.
    private static double NormalCdf(double z)
    {
        var sign = z < 0 ? -1.0 : 1.0;
        var x = Math.Abs(z) / Math.Sqrt(2.0);

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var t = 1.0 / (1.0 + (p * x));

        var y = 1.0 - (((((((a5 * t) + a4) * t) + a3) * t) + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + (sign * y));
    }
}

/// An item PRAXIS could ask.
public sealed record Candidate(
    string Id,
    double A,
    double B,
    EvidenceType EvidenceType,

    /// Minutes. **A BUILD costs twenty; an APPLY costs forty seconds** — and an engine that ignores
    /// that will fill a fifteen-minute session with one item and call it efficient.
    double CostMinutes,

    /// Misconception **keys** this item can diagnose. The hunt (I-6) is aimed with these.
    IReadOnlySet<string> Diagnoses,

    /// How often this item has been served across the population, in [0, 1]. Sympson–Hetter: **an
    /// item everybody sees is an item everybody has heard about.**
    double Exposure = 0.0,

    double C = 0.0);

/// Everything the selector needs to know about the learner, right now.
public sealed record SelectionContext(
    /// **Starts from the Twin, never from zero** (I-1). A standard CAT starts every learner at the
    /// population mean, which is a way of announcing it has decided to know nothing about them.
    Ability Ability,

    /// The contract threshold, in logits. **This is the question the whole session is trying to
    /// answer** — not *"how good are you"*, but *"do you hold this"*.
    double HoldThreshold,

    /// Misconception key → P(the learner holds it). Uncertain ones are worth probing; settled ones
    /// are not.
    IReadOnlyDictionary<string, double> OpenMisconceptions,

    /// Rising fatigue penalises expensive items. **Tiredness is not ignorance** (see `Adaptation`).
    double Fatigue = 0.0);
