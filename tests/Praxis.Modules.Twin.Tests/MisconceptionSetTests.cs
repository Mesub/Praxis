using Praxis.Modules.Twin.Learning;

namespace Praxis.Modules.Twin.Tests;

/// The difference between a quiz app and a Twin. A quiz app knows your score. This knows that you
/// believe *cosine similarity is unaffected by whether you normalised the vectors* — a claim that
/// can be taught against, generated against, and watched until it dies.
public sealed class MisconceptionSetTests
{
    private static readonly DateTime Day1 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_misconception_is_a_named_belief_stated_as_the_learner_would_state_it()
    {
        // First person, on purpose. It has to be recognisable to them, or they will not believe
        // they hold it — and a misconception the learner rejects is one they keep.
        var m = MisconceptionState.Unseen(
            "COSINE_IGNORES_NORMALISATION",
            "Cosine similarity gives the same answer whether or not I normalised the vectors.");

        Assert.Equal("cosine_ignores_normalisation", m.Key);
        Assert.StartsWith("Cosine similarity", m.Statement, StringComparison.Ordinal);

        // Named, but not yet observed. We do not accuse people of things they have not done.
        Assert.False(m.IsActive);
        Assert.Equal(0, m.TimesObserved);
    }

    [Fact]
    public void Observing_it_makes_it_active()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        Assert.True(m.IsActive);
        Assert.Equal(1, m.TimesObserved);
        Assert.Equal(Day1, m.FirstObservedAt);
        Assert.Equal(Day1, m.LastObservedAt);
    }

    /// **The rule the whole product turns on, applied to misconceptions.**
    ///
    /// Explaining to someone that they are wrong is the least reliable way of discovering whether
    /// they still are. Being taught is not evidence — not for mastery, and not for its absence.
    [Fact]
    public void Being_told_you_are_wrong_does_not_retire_a_misconception()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        // The tutor explains it. Beautifully. Three times.
        m = MisconceptionSet.Taught(m);
        m = MisconceptionSet.Taught(m);
        m = MisconceptionSet.Taught(m);

        Assert.True(
            m.IsActive,
            "Instruction retired a misconception. Being taught is not evidence, and a Twin that "
            + "believes otherwise will keep telling a learner they are fine.");
    }

    [Fact]
    public void One_contradiction_is_not_enough()
    {
        // Once could be luck, or a lucky guess, or the phrasing of one item. A Twin that forgives
        // too easily keeps telling the learner they are fine.
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        m = MisconceptionSet.Contradict(m, Day1.AddDays(1));

        Assert.True(m.IsActive);
        Assert.Equal(1, m.TimesContradicted);
    }

    [Fact]
    public void Two_clean_contradictions_retire_it()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        m = MisconceptionSet.Contradict(m, Day1.AddDays(1));
        m = MisconceptionSet.Contradict(m, Day1.AddDays(3));

        Assert.False(m.IsActive);
        Assert.Equal(Day1.AddDays(3), m.RetiredAt);
    }

    /// **Misconceptions relapse.** Under time pressure, under fatigue, on a variant that does not
    /// look like the one they were corrected on. Pretending otherwise is how a person ships the
    /// same bug twice.
    [Fact]
    public void A_retired_misconception_comes_back_if_they_show_it_again()
    {
        var m = Retired();

        m = MisconceptionSet.Observe(m, Day1.AddDays(30));

        Assert.True(m.IsActive);
        Assert.True(m.HasRelapsed, "The Twin did not notice this was a relapse rather than a new fault.");
    }

    /// **The subtle one, and it took a moment to see.**
    ///
    /// If contradictions accumulated across relapses, a learner who showed the misconception six
    /// times and was contradicted once after each would be recorded as *cured* — because the
    /// contradictions would eventually total two. They would have demonstrated nothing except
    /// inconsistency, and PRAXIS would have signed it off.
    ///
    /// Showing it again resets the road out. You earn your way out from scratch.
    [Fact]
    public void Relapsing_resets_the_road_out()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        // One clean contradiction. Nearly there.
        m = MisconceptionSet.Contradict(m, Day1.AddDays(1));
        Assert.Equal(1, m.TimesContradicted);

        // ...and then they do it again.
        m = MisconceptionSet.Observe(m, Day1.AddDays(2));

        Assert.Equal(
            0, m.TimesContradicted);

        // So one more contradiction is NOT enough to retire it now.
        m = MisconceptionSet.Contradict(m, Day1.AddDays(3));

        Assert.True(
            m.IsActive,
            "A learner who relapsed and was then contradicted once was recorded as cured. They "
            + "demonstrated nothing except inconsistency.");
    }

    [Fact]
    public void Contradicting_a_retired_misconception_changes_nothing()
    {
        var m = Retired();
        var before = m;

        m = MisconceptionSet.Contradict(m, Day1.AddDays(10));

        Assert.Equal(before, m);
    }

    // ── The penalty ──────────────────────────────────────────────────────────

    /// A learner carrying a confidently-held wrong model is **worse off** than one who knows they
    /// do not know — because they will not ask. So an active misconception suppresses belief in the
    /// concept it sits under, however well they score around it.
    [Fact]
    public void An_active_misconception_suppresses_belief_in_its_concept()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);

        Assert.True(MisconceptionSet.BeliefPenalty(m) > 0);
    }

    [Fact]
    public void A_retired_misconception_costs_nothing()
    {
        Assert.Equal(0.0, MisconceptionSet.BeliefPenalty(Retired()));
    }

    [Fact]
    public void Showing_it_repeatedly_is_worse_than_showing_it_once()
    {
        var once = MisconceptionSet.Observe(Fresh(), Day1);

        var thrice = once;
        thrice = MisconceptionSet.Observe(thrice, Day1.AddDays(1));
        thrice = MisconceptionSet.Observe(thrice, Day1.AddDays(2));

        Assert.True(MisconceptionSet.BeliefPenalty(thrice) > MisconceptionSet.BeliefPenalty(once));
    }

    [Fact]
    public void The_penalty_saturates_because_there_is_nothing_to_learn_from_the_fifth_observation()
    {
        var m = Fresh();

        for (var i = 0; i < 20; i++)
        {
            m = MisconceptionSet.Observe(m, Day1.AddDays(i));
        }

        Assert.Equal(MisconceptionSet.MaxPenalty, MisconceptionSet.BeliefPenalty(m), precision: 9);
    }

    // ── Replayability ────────────────────────────────────────────────────────

    [Fact]
    public void The_same_history_produces_the_same_misconception()
    {
        // Otherwise a rebuild produces a different learner.
        Assert.Equal(Story(), Story());

        static MisconceptionState Story()
        {
            var m = MisconceptionState.Unseen("confuses_recall_with_precision", "They are the same thing.");

            m = MisconceptionSet.Observe(m, Day1);
            m = MisconceptionSet.Contradict(m, Day1.AddDays(2));
            m = MisconceptionSet.Observe(m, Day1.AddDays(5));      // relapse
            m = MisconceptionSet.Taught(m);                        // changes nothing
            m = MisconceptionSet.Contradict(m, Day1.AddDays(9));
            m = MisconceptionSet.Contradict(m, Day1.AddDays(14));

            return m;
        }
    }

    [Fact]
    public void A_full_story_ends_where_it_should()
    {
        // Observed, contradicted once, relapsed, then two clean contradictions. Retired — and the
        // record still shows it was a relapse, which is what the tutor needs to know.
        var m = MisconceptionState.Unseen("cosine_ignores_normalisation", "Normalisation does not matter.");

        m = MisconceptionSet.Observe(m, Day1);
        m = MisconceptionSet.Contradict(m, Day1.AddDays(2));
        m = MisconceptionSet.Observe(m, Day1.AddDays(5));
        m = MisconceptionSet.Contradict(m, Day1.AddDays(9));
        m = MisconceptionSet.Contradict(m, Day1.AddDays(14));

        Assert.False(m.IsActive);
        Assert.Equal(2, m.TimesObserved);
        Assert.Equal(Day1.AddDays(14), m.RetiredAt);
        Assert.Equal(0.0, MisconceptionSet.BeliefPenalty(m));
    }

    private static MisconceptionState Fresh()
        => MisconceptionState.Unseen(
            "cosine_ignores_normalisation",
            "Cosine similarity gives the same answer whether or not I normalised the vectors.");

    private static MisconceptionState Retired()
    {
        var m = MisconceptionSet.Observe(Fresh(), Day1);
        m = MisconceptionSet.Contradict(m, Day1.AddDays(1));
        m = MisconceptionSet.Contradict(m, Day1.AddDays(3));

        return m;
    }
}
