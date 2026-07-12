using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Domain;

/// **The Answer Budget.** A finite number of times PRAXIS will help you with one concept.
///
/// ## It is not a paywall, and it is not a game mechanic
///
/// It would be trivially easy to build this as one, and every instinct in the industry points that
/// way: *out of hints? watch an ad. Buy a hint pack. Come back tomorrow for three free hints.* Every
/// one of those would work commercially, and every one of them would destroy the thing the budget
/// exists to protect.
///
/// The budget is doing **two entirely different jobs**, and both of them require that it be
/// genuinely scarce.
///
/// ### 1. It is pedagogy (I-5)
///
/// Unlimited hints do not accelerate learning; they *replace* it. A learner who can always get the
/// next nudge for free never sits in the productive discomfort where the learning actually happens —
/// they outsource the struggle, feel like they are progressing, and produce nothing they could
/// reproduce alone. The friction is not a flaw in the product. **The friction is the product.**
///
/// ### 2. It is a security control (C-5)
///
/// This one is less obvious, and it is the reason the budget cannot be replenished by anything the
/// learner can choose to do.
///
/// The tutor cannot give the answer, because the answer is not in its context (M5.3). But the tutor
/// can *ask the oracle* whether a claim is right, and an oracle is a channel. Given unlimited
/// questions, a patient learner — or a tutor jailbroken into acting as their proxy — can grind
/// guesses against it and **binary-search the answer out**, and everyone involved could still say,
/// truthfully, that the answer was never in the prompt.
///
/// **Two things close that attack, and they only work together:**
///
/// * items are never repeated (M5.5 — novelty is a hash, not a hope), so you cannot grind *one*
///   answer; and
/// * help on a concept is **finite**, so you cannot grind *around* it either.
///
/// Sell a refill, and the second lock opens. That is why there is no `Grant`, no `Refill`, no
/// `AddHints`, and no admin override on this type — and why the tests assert their absence rather
/// than trusting this comment.
public sealed class AnswerBudget : BaseEntity
{
    /// **The default allowance for one concept** (CANON §1.3).
    ///
    /// 120, and the whole ladder from `Probe` to `PartialScaffold` costs 93. So a learner can be
    /// walked all the way down **exactly once**, with a little left over — and then they are out, on
    /// this concept, until they *produce* something.
    ///
    /// That is the number doing the work. Hint costs are calibrated to be roughly **one production
    /// event apart**: a learner who produces at the expected rate can climb the ladder indefinitely
    /// and never go broke, and a learner who only consumes is broke in about four rungs.
    ///
    /// **That asymmetry is I-5.** It is not a limit on help; it is a limit on *unearned* help.
    public const int DefaultAllowance = 120;

    public Guid LearnerId { get; private set; }
    public Guid ConceptId { get; private set; }

    /// What they started with. Set once, at creation.
    public int Allowance { get; private set; } = DefaultAllowance;

    /// Monotonic. **It only ever goes up**, and no event in the catalogue can lower it.
    public int Spent { get; private set; }

    public int Remaining => Math.Max(0, Allowance - Spent);

    public bool IsExhausted => Remaining <= 0;

    /// The most expensive rung ever taken on this concept. The Twin uses it to discount credit: you
    /// did not produce that alone (I-5), and the Ledger is entitled to know.
    public HintRung? DeepestRung { get; private set; }

    public int HintsTaken { get; private set; }

    private AnswerBudget() { }   // EF

    public static AnswerBudget For(Guid learnerId, Guid conceptId, int allowance = DefaultAllowance)
    {
        if (allowance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowance), allowance, "A negative allowance is a debt, not a budget.");
        }

        return new AnswerBudget
        {
            LearnerId = learnerId,
            ConceptId = conceptId,
            Allowance = allowance,
        };
    }

    /// Can they afford this rung? Asked *before* the tutor commits to giving it.
    public bool CanAfford(HintRung rung) => HintCost.Of(rung) <= Remaining;

    /// **Spend. The only mutator on this type, and it only subtracts.**
    ///
    /// `internal` because only the projection may call it, and only in reply to a `HintTaken` event
    /// that is already on the log. A budget that could be spent by a controller would be a budget
    /// that could be *un*-spent by one.
    internal Result Spend(HintRung rung)
    {
        var cost = HintCost.Of(rung);

        if (cost > Remaining)
        {
            // Refused, and **not** clamped to zero. Silently giving a hint they could not afford and
            // recording the budget as empty would be the worst of both: they got the help, and the
            // Ledger says they did not.
            return Result.Invalid(
                $"That hint costs {cost} and there is {Remaining} left on this concept. PRAXIS has "
                + "helped as much as it is going to. What you produce now, you produce yourself — "
                + "which was always the point.");
        }

        Spent += cost;
        HintsTaken++;

        if (DeepestRung is null || rung > DeepestRung)
        {
            DeepestRung = rung;
        }

        return Result.Success();
    }

    /// Replay support: a projection rebuild starts from zero and re-applies history (M1.6).
    internal void Reset()
    {
        Spent = 0;
        HintsTaken = 0;
        DeepestRung = null;
    }
}

/// What each rung costs (CANON §1.2).
///
/// **The curve is steep, and it has to be.** Under a flat price a learner optimising against the
/// cost jumps straight to the strongest scaffold every time — which is precisely the behaviour the
/// ladder exists to prevent. So each rung costs roughly double the one above it, and the difference
/// between orienting yourself (free) and being handed the skeleton (45) is the difference between
/// a nudge and a rescue.
public static class HintCost
{
    /// The surcharge for asking for the same rung again on the same item (**LR-2**).
    ///
    /// Not a punishment — a *price signal*. Re-reading a hint you did not understand is fine and
    /// cheap; wearing the same rung down until it accidentally says something new is not, and this
    /// is the only thing standing between the tutor and a learner who has discovered that asking the
    /// same question eleven times gets a slightly different answer each time.
    public const double RepeatMultiplier = 1.5;

    public static int Of(HintRung rung) => rung switch
    {
        // **Free, and unlimited.** "What kind of problem is this? What do I actually know?" is not a
        // hint — it is a skill, and a skill you are charged for is a skill you learn not to use.
        HintRung.Orient => 0,

        HintRung.Probe => 6,
        HintRung.Analogy => 14,
        HintRung.WorkedExample => 28,

        // The structure, with the hole still in it. The most expensive thing PRAXIS will do for you,
        // and it *still* does not fill the hole.
        HintRung.PartialScaffold => 45,

        _ => throw new ArgumentOutOfRangeException(
            nameof(rung), rung,
            "An unpriced rung would be a free one, and a free rung is an unlimited one."),
    };

    /// **LR-2.** 1.5×, rounded up. Free stays free — charging for a second orientation would teach
    /// exactly the wrong lesson.
    public static int Repeated(HintRung rung)
        => (int)Math.Ceiling(Of(rung) * RepeatMultiplier);
}
