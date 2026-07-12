using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **Eradication is *earned*, not declared.**
///
/// This is the sentence the whole file exists to enforce, and it is the one every other product in
/// this industry gets wrong — usually by not asking the question at all.
///
/// A learner holds a wrong belief. You explain why it is wrong. They nod. They select the right
/// option on the next multiple-choice question. **Every system in the world now marks that
/// misconception as fixed**, and it is not fixed — it is *asleep*. It will wake up in six weeks, on
/// a variant that does not look like the one they were corrected on, under time pressure, and they
/// will make exactly the same mistake and have no idea why.
///
/// Being told you are wrong is not evidence that you have stopped being wrong.
///
/// ## What it actually takes to close one (CANON §6.4)
///
/// | Requirement | Why |
/// |---|---|
/// | **≥ 3 targeted items** | Items whose distractors specifically diagnose *this* belief. |
/// | **≥ 2 sessions** | One good day is not a repair. |
/// | **≥ 14 days** | **A repair that does not survive a sleep cycle is a performance, not a learning.** |
/// | **≥ 1 CRITIQUE** where the injected flaw *is* this misconception | Spotting it in *somebody else's* work is strictly harder than not making it yourself. |
/// | **They must NAME it** | *"There's a bug"* is not the same as *"he's treating the level as a barrier when it's a finite pool of liquidity."* Selecting the right option proves nothing about what they believe. |
/// | **A different modality** | **Misconceptions hide in the modality they were learned in.** |
///
/// ## And even then it is not permanent
///
/// **Nothing in PRAXIS is permanent** (I-2). An eradicated misconception is re-probed forever, at
/// the concept's rep cadence, and it can relapse — because people do. A product that marks a wrong
/// belief as *cured* has told a learner something it cannot possibly know.
public static class MisconceptionLifecycle
{
    /// One hit is a suspicion. **Two independent hits is a diagnosis** — and only then does PRAXIS
    /// start spending the learner's time on it.
    public const int HitsToConfirm = 2;

    /// Clean answers on the discriminating dimension needed to dismiss a suspicion as noise. A
    /// suspicion PRAXIS never lets go of is a suspicion that becomes a fact by attrition.
    public const int CleanItemsToDismiss = 3;

    public const int FailedRemediationsToPersistent = 3;

    public static MisconceptionStatus AfterHit(MisconceptionStatus status, int independentHits)
        => status switch
        {
            // A relapse from *any* resolved state throws it straight back to CONFIRMED. It was never
            // a new suspicion — it is the same old belief, and PRAXIS already knows its name.
            MisconceptionStatus.Remediated or MisconceptionStatus.Eradicated
                or MisconceptionStatus.Challenged => MisconceptionStatus.Confirmed,

            _ when independentHits >= HitsToConfirm => MisconceptionStatus.Confirmed,

            _ => MisconceptionStatus.Suspected,
        };
}

/// **The receipts.** What a learner has actually done towards killing one specific wrong belief.
///
/// Every field here is a *fact*, and the verdict is arithmetic over facts. There is no
/// `MarkAsFixed`, no `Override`, and no place for anybody's opinion — because the moment somebody's
/// opinion can close a misconception, the thing that closes it will be *fatigue*.
public sealed record EradicationEvidence(
    /// Items whose distractors specifically diagnose **this** belief. Passing a general item about
    /// the concept proves nothing: they may simply have avoided the trap this time.
    int TargetedItemsPassed,

    /// **One good day is not a repair.**
    int DistinctSessions,

    /// **A repair that does not survive a sleep cycle is a performance, not a learning.**
    int DaysSpanned,

    /// A CRITIQUE item where the **injected flaw is this misconception**. Spotting it in somebody
    /// else's work is strictly harder than not making it yourself — and it is the only way to find
    /// out whether they can *see* it, rather than merely avoid it.
    bool CritiquedItInSomeoneElsesWork,

    /// **They named it, in their own words, and repaired it.** Selecting the right option proves
    /// nothing about what a person believes; it proves they can select options.
    bool NamedAndRepairedIt,

    /// **Misconceptions hide in the modality they were learned in.** A learner corrected on prose
    /// will make the identical error in a diagram and not recognise it as the same mistake.
    bool SurvivedADifferentModality)
{
    public static readonly EradicationEvidence None = new(0, 0, 0, false, false, false);

    public const int RequiredTargetedItems = 3;
    public const int RequiredSessions = 2;
    public const int RequiredDays = 14;

    /// **Everything that is still missing.** Empty means it is genuinely gone — as far as anyone can
    /// know, which is not the same as forever.
    public IReadOnlyList<string> StillMissing()
    {
        var missing = new List<string>();

        if (TargetedItemsPassed < RequiredTargetedItems)
        {
            missing.Add(
                $"{RequiredTargetedItems - TargetedItemsPassed} more item(s) that specifically "
                + "target this belief. Getting a general question right may just mean you avoided "
                + "the trap this time.");
        }

        if (DistinctSessions < RequiredSessions)
        {
            missing.Add("A second session. One good day is not a repair.");
        }

        if (DaysSpanned < RequiredDays)
        {
            missing.Add(
                $"{RequiredDays - DaysSpanned} more day(s). A repair that does not survive a sleep "
                + "cycle is a performance, not a learning.");
        }

        if (!CritiquedItInSomeoneElsesWork)
        {
            missing.Add(
                "You have not yet caught this mistake in somebody else's work. Spotting it there is "
                + "strictly harder than not making it yourself.");
        }

        if (!NamedAndRepairedIt)
        {
            missing.Add(
                "You have not named it. \"There's a bug\" is not the same as saying what the wrong "
                + "rule was — and picking the right option proves only that you can pick options.");
        }

        if (!SurvivedADifferentModality)
        {
            missing.Add(
                "It has only been tested in one form. Misconceptions hide in the modality they were "
                + "learned in.");
        }

        return missing;
    }

    /// **The only way a misconception gets closed.** Not by being explained. Not by a nod. Not by an
    /// admin flag on a bad day.
    public bool IsEarned => StillMissing().Count == 0;

    /// **The refusal, said out loud.** A learner asking *"can we call this fixed?"* gets an honest
    /// list, not a "not yet".
    public Result Eradicate()
    {
        var missing = StillMissing();

        return missing.Count == 0
            ? Result.Success()
            : Result.Invalid(
                "This is not fixed yet. Being told you are wrong is not evidence that you have "
                + "stopped being wrong — and PRAXIS is not going to write down that a belief is "
                + "gone because you nodded at an explanation of it.",
                missing);
    }
}

/// The lifecycle of one wrong belief in one learner (CANON §6.4).
public enum MisconceptionStatus
{
    /// One hit. It might have been noise, the phrasing of an item, or a bad afternoon — and PRAXIS
    /// does not spend a learner's time on a suspicion.
    Suspected = 1,

    /// **Two independent hits.** Now it is a diagnosis: E4 schedules a targeted session, E6 builds
    /// distractors from it, E5 arms contrasting cases.
    Confirmed = 2,

    /// **The learner produced the correct discrimination themselves** — through cognitive conflict,
    /// a contrasting case, or catching their own error. This is the only transition in the lifecycle
    /// the learner performs rather than receives, and it is the one that predicts everything after.
    Challenged = 3,

    /// They passed the eradication set. **Not "cured" — passed.**
    Remediated = 4,

    /// Survived further reps at ≥30 and ≥90 days, including a CRITIQUE with the flaw injected.
    ///
    /// **And it is still not permanent.** Nothing in PRAXIS is (I-2). It is re-probed forever, and it
    /// can come back — because people do.
    Eradicated = 5,

    /// Three failed remediations. **This is not the learner's failure. It is ours.** Escalate: a
    /// different representation, a different pedagogy, a human being — or an honest report that this
    /// concept is out of reach for now, and a plan built around it.
    Persistent = 6,
}
