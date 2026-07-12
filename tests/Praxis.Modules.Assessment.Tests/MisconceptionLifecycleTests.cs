using System.Reflection;
using Praxis.Modules.Assessment.Domain;

namespace Praxis.Modules.Assessment.Tests;

/// **Eradication is earned, not declared.**
///
/// A learner holds a wrong belief. You explain why it is wrong. They nod. They pick the right option
/// on the next question. **Every system in the world now marks that misconception as fixed** — and it
/// is not fixed. It is *asleep*. It will wake up in six weeks, on a variant that does not look like
/// the one they were corrected on, under time pressure, and they will make exactly the same mistake
/// and have no idea why.
///
/// **Being told you are wrong is not evidence that you have stopped being wrong.**
public sealed class MisconceptionLifecycleTests
{
    private static EradicationEvidence Complete => new(
        TargetedItemsPassed: 3,
        DistinctSessions: 2,
        DaysSpanned: 14,
        CritiquedItInSomeoneElsesWork: true,
        NamedAndRepairedIt: true,
        SurvivedADifferentModality: true);

    // ── One hit is a suspicion. Two is a diagnosis. ──────────────────────────

    [Fact]
    public void One_hit_is_a_suspicion_and_PRAXIS_does_not_act_on_it()
    {
        Assert.Equal(
            MisconceptionStatus.Suspected,
            MisconceptionLifecycle.AfterHit(MisconceptionStatus.Suspected, independentHits: 1));
    }

    [Fact]
    public void Two_independent_hits_is_a_diagnosis()
    {
        Assert.Equal(
            MisconceptionStatus.Confirmed,
            MisconceptionLifecycle.AfterHit(MisconceptionStatus.Suspected, independentHits: 2));
    }

    /// **A relapse is not a new suspicion.** It is the same old belief, and PRAXIS already knows its
    /// name — so it goes straight back to `Confirmed`, not back to the bottom of the ladder.
    ///
    /// A system that treats every relapse as a fresh sighting will spend forever re-diagnosing a
    /// belief it identified months ago.
    [Theory]
    [InlineData(MisconceptionStatus.Remediated)]
    [InlineData(MisconceptionStatus.Eradicated)]
    [InlineData(MisconceptionStatus.Challenged)]
    public void A_relapse_goes_straight_back_to_confirmed(MisconceptionStatus from)
    {
        Assert.Equal(
            MisconceptionStatus.Confirmed,
            MisconceptionLifecycle.AfterHit(from, independentHits: 1));
    }

    // ── The eradication set ─────────────────────────────────────────────────

    /// **The whole point.** A learner who has done everything can close it — and that is the *only*
    /// way it closes.
    [Fact]
    public void A_learner_who_has_actually_done_the_work_can_close_it()
    {
        Assert.True(Complete.IsEarned);
        Assert.True(Complete.Eradicate().IsSuccess);
        Assert.Empty(Complete.StillMissing());
    }

    [Fact]
    public void Nothing_at_all_earns_nothing()
    {
        var nothing = EradicationEvidence.None;

        Assert.False(nothing.IsEarned);

        // And it says *everything* that is missing, not just the first thing.
        Assert.Equal(6, nothing.StillMissing().Count);
    }

    /// **One good day is not a repair.** Three targeted items in a single afternoon is a performance.
    [Fact]
    public void Three_items_in_one_session_is_not_a_repair()
    {
        var oneGoodDay = Complete with { DistinctSessions = 1 };

        Assert.False(oneGoodDay.IsEarned);

        Assert.Contains(
            oneGoodDay.StillMissing(),
            m => m.Contains("One good day is not a repair", StringComparison.Ordinal));
    }

    /// **A repair that does not survive a sleep cycle is a performance, not a learning.**
    ///
    /// This is the requirement that makes the whole thing honest. Everything a learner can do in one
    /// sitting, they can do by holding the correction in working memory — and working memory is
    /// empty in the morning.
    [Fact]
    public void A_repair_has_to_survive_a_sleep_cycle()
    {
        var sameWeek = Complete with { DaysSpanned = 3 };

        Assert.False(sameWeek.IsEarned);

        Assert.Contains(
            sameWeek.StillMissing(),
            m => m.Contains("survive a sleep cycle", StringComparison.Ordinal));
    }

    /// **Spotting it in somebody else's work is strictly harder than not making it yourself.**
    ///
    /// A learner can avoid a trap without ever seeing it. Catching it in someone *else's* reasoning
    /// is the only test that finds out whether they can actually *see* the thing.
    [Fact]
    public void They_have_to_catch_it_in_somebody_elses_work()
    {
        var neverCritiqued = Complete with { CritiquedItInSomeoneElsesWork = false };

        Assert.False(neverCritiqued.IsEarned);

        Assert.Contains(
            neverCritiqued.StillMissing(),
            m => m.Contains("somebody else's work", StringComparison.Ordinal));
    }

    /// **"There's a bug" is not the same as saying what the wrong rule was.**
    ///
    /// Picking the right option proves that a learner can pick options. Naming the error proves they
    /// know what it *was* — and only one of those survives contact with a novel problem.
    [Fact]
    public void They_have_to_name_it_not_just_avoid_it()
    {
        var neverNamed = Complete with { NamedAndRepairedIt = false };

        Assert.False(neverNamed.IsEarned);

        Assert.Contains(
            neverNamed.StillMissing(),
            m => m.Contains("proves only that you can pick options", StringComparison.Ordinal));
    }

    /// **Misconceptions hide in the modality they were learned in.**
    ///
    /// A learner corrected in prose will make the identical error in a diagram — and will not
    /// recognise it as the same mistake, because to them it is not.
    [Fact]
    public void It_has_to_survive_a_different_modality()
    {
        var oneModality = Complete with { SurvivedADifferentModality = false };

        Assert.False(oneModality.IsEarned);

        Assert.Contains(
            oneModality.StillMissing(),
            m => m.Contains("hide in the modality", StringComparison.Ordinal));
    }

    /// **The refusal is a list, not a "no".**
    ///
    /// A learner asking *"can we call this fixed?"* is owed the receipts — exactly what is still
    /// outstanding — rather than a closed door.
    [Fact]
    public void The_refusal_says_exactly_what_is_still_missing()
    {
        var partway = new EradicationEvidence(
            TargetedItemsPassed: 2,
            DistinctSessions: 2,
            DaysSpanned: 20,
            CritiquedItInSomeoneElsesWork: true,
            NamedAndRepairedIt: false,
            SurvivedADifferentModality: true);

        var refused = partway.Eradicate();

        Assert.False(refused.IsSuccess);

        Assert.Contains(
            "Being told you are wrong is not evidence", refused.Message!, StringComparison.Ordinal);

        // Two things outstanding, and it names both.
        Assert.Equal(2, refused.Errors.Count);
    }

    // ── There is no back door ───────────────────────────────────────────────

    /// **The test that stops a bad afternoon closing a misconception.**
    ///
    /// There is no `MarkAsFixed`, no `Override`, no `ForceEradicate`. The moment somebody's *opinion*
    /// can close a wrong belief, the thing that closes it will be **fatigue** — a support queue, a
    /// deadline, a learner who complained.
    [Fact]
    public void There_is_no_way_to_declare_a_misconception_fixed()
    {
        var methods = typeof(EradicationEvidence)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(EradicationEvidence))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] forbidden =
        [
            "MarkAsFixed", "Override", "ForceEradicate", "Declare", "Close", "Dismiss", "Waive",
        ];

        foreach (var name in forbidden)
        {
            Assert.False(
                mutatorExists(name),
                $"EradicationEvidence.{name} exists. A misconception can now be closed by somebody's "
                + "opinion — and the opinion that eventually closes it will be fatigue.");
        }

        bool mutatorExists(string name) => methods.Contains(name);
    }

    /// **Eradicated is not permanent, because nothing in PRAXIS is** (I-2).
    ///
    /// A product that marks a wrong belief as *cured* has told a learner something it cannot possibly
    /// know. It is re-probed forever, and it can come back — because people do.
    [Fact]
    public void Even_eradicated_is_not_permanent()
    {
        Assert.Equal(
            MisconceptionStatus.Confirmed,
            MisconceptionLifecycle.AfterHit(MisconceptionStatus.Eradicated, independentHits: 1));
    }

    /// **Three failed remediations is not the learner's failure. It is ours.**
    [Fact]
    public void A_persistent_misconception_is_an_escalation_not_a_verdict_on_the_learner()
    {
        Assert.Equal(3, MisconceptionLifecycle.FailedRemediationsToPersistent);

        Assert.Contains(MisconceptionStatus.Persistent, Enum.GetValues<MisconceptionStatus>());
    }

    /// A suspicion PRAXIS never lets go of is a suspicion that **becomes a fact by attrition**.
    [Fact]
    public void A_suspicion_can_be_dismissed_as_noise()
    {
        Assert.True(MisconceptionLifecycle.CleanItemsToDismiss >= 3);
    }
}
