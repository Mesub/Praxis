using Praxis.Modules.Twin.Domain;
using Praxis.Modules.Twin.Features.States;

namespace Praxis.Modules.Twin.Tests;

/// The read model. Decay is applied here, on the way out — so that refreshing an hour later shows a
/// lower number, because an hour of forgetting actually happened.
public sealed class LiveTwinTests
{
    private static readonly DateTime Proved = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void At_the_moment_of_proof_nothing_has_rotted_yet()
    {
        var view = LiveTwin.Project(Held(stability: 14), [], Proved);

        Assert.Equal(1.0, view.Retrievability, precision: 6);
        Assert.Equal(view.PKnowsAtLastEvidence, view.PKnows, precision: 6);
    }

    /// **The number nobody else in this industry shows you.**
    ///
    /// The learner is still "held" — the Ledger says they proved it — and it is going. A
    /// certificate that cannot say this is a certificate that is lying.
    [Fact]
    public void A_held_concept_is_visibly_rotting_a_month_later()
    {
        var view = LiveTwin.Project(Held(stability: 14), [], Proved.AddDays(30));

        Assert.True(view.IsHeld, "It is still held — they did prove it.");
        Assert.True(view.Retrievability < 0.90, $"A month on, retrievability was still {view.Retrievability:F2}.");
        Assert.True(view.PKnows < view.PKnowsAtLastEvidence);
        Assert.True(LiveTwin.IsDecaying(view));
    }

    [Fact]
    public void Left_long_enough_it_has_gone_whatever_the_ledger_says()
    {
        var view = LiveTwin.Project(Held(stability: 10), [], Proved.AddDays(365));

        Assert.True(view.IsHeld);           // the Ledger's claim stands: they *did* prove it
        Assert.True(LiveTwin.HasLapsed(view));   // and today they almost certainly could not
        Assert.True(view.DaysUntilDue < 0, "It is overdue and the forecast does not say so.");
    }

    [Fact]
    public void Refreshing_an_hour_later_shows_a_lower_number()
    {
        // Not a cron job. A belief that only decays when a scheduled task remembers to run is not a
        // model of forgetting; it is a model of cron.
        var state = Held(stability: 7);

        var now = LiveTwin.Project(state, [], Proved.AddDays(10));
        var later = LiveTwin.Project(state, [], Proved.AddDays(10).AddHours(1));

        Assert.True(later.PKnows < now.PKnows);
    }

    [Fact]
    public void An_unseen_concept_reports_honestly_that_nothing_is_known()
    {
        var view = LiveTwin.Project(
            ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7()), [], Proved);

        Assert.False(view.IsHeld);
        Assert.Equal(0.0, view.MasteryCredit);
        Assert.Equal(0.0, view.Retrievability);
        Assert.Equal(0, view.EvidenceCount);

        // And it does not pretend to have measured their calibration.
        Assert.False(view.CalibrationIsMeaningful);
    }

    [Fact]
    public void Calibration_is_not_reported_until_it_means_something()
    {
        // Reporting a Brier score on n=2 would be exactly the overconfidence the metric exists to
        // catch.
        var barely = Held(stability: 10);
        barely.Set(0.9, 0.9, true, 10, 5, Proved, Proved.AddDays(10), 0.8, 0.9, 0.05,
            calibrationSamples: 2, assurance: 1.0, evidenceCount: 1);

        Assert.False(LiveTwin.Project(barely, [], Proved).CalibrationIsMeaningful);

        barely.Set(0.9, 0.9, true, 10, 5, Proved, Proved.AddDays(10), 0.8, 0.9, 0.05,
            calibrationSamples: 12, assurance: 1.0, evidenceCount: 1);

        Assert.True(LiveTwin.Project(barely, [], Proved).CalibrationIsMeaningful);
    }

    [Fact]
    public void The_confidence_gap_is_surfaced_because_the_learner_needs_to_see_it()
    {
        var s = Held(stability: 10);
        s.Set(0.5, 0.85, true, 10, 5, Proved, Proved.AddDays(10),
            meanStatedConfidence: 0.95, meanCorrectness: 0.55,
            brierScore: 0.30, calibrationSamples: 20, assurance: 0.9, evidenceCount: 3);

        var view = LiveTwin.Project(s, [], Proved);

        Assert.True(view.ConfidenceGap > 0.35, "They think they are far better than they are, and the API is not saying so.");
    }

    [Fact]
    public void Active_misconceptions_are_surfaced_and_retired_ones_are_not()
    {
        var learner = Guid.CreateVersion7();
        var concept = Guid.CreateVersion7();

        var active = Misconception.Observed(learner, concept, "cosine_ignores_normalisation",
            "Normalisation does not affect cosine similarity.", Proved);

        var retired = Misconception.Observed(learner, concept, "confuses_recall_with_precision",
            "They are the same thing.", Proved);
        retired.Contradict();
        retired.Contradict();

        var view = LiveTwin.Project(Held(stability: 10), [active, retired], Proved);

        Assert.Single(view.ActiveMisconceptions);
        Assert.Equal("cosine_ignores_normalisation", view.ActiveMisconceptions[0].Key);
    }

    private static ConceptState Held(double stability)
    {
        var s = ConceptState.Unseen(Guid.CreateVersion7(), Guid.CreateVersion7());

        s.Set(
            pKnows: 0.90, masteryCredit: 0.90, isHeld: true,
            stability: stability, difficulty: 5,
            lastEvidenceAt: Proved,
            nextDueAt: Proved.AddDays(stability),
            meanStatedConfidence: 0.8, meanCorrectness: 0.85,
            brierScore: 0.08, calibrationSamples: 10,
            assurance: 1.0, evidenceCount: 3);

        return s;
    }
}
