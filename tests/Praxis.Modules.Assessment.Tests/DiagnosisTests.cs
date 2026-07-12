using System.Reflection;
using Praxis.Modules.Assessment.Domain;

namespace Praxis.Modules.Assessment.Tests;

/// **"Here is what you think you know, and don't."**
///
/// Being wrong is survivable. Everybody is wrong about most things, and the things you *know* you do
/// not know are harmless — you look those up, you ask, you go carefully.
///
/// **The dangerous ones are the things you are sure about and wrong about.** You never look those
/// up. You build on them, everything you build is subtly crooked, and when it falls over you blame
/// the wrong thing — because the beam you trusted was never the one you were checking.
///
/// This is the only list in the product a learner could not have written for themselves. **By
/// definition: you cannot list your own blind spots.**
public sealed class DiagnosisTests
{
    private static ConceptFinding Finding(
        string slug, double confidence, double competence, int samples = 10)
        => Diagnosis.Assess(Guid.CreateVersion7(), slug, confidence, competence, samples);

    // ── The list that matters ────────────────────────────────────────────────

    [Fact]
    public void Sure_and_wrong_is_a_blind_spot()
    {
        var finding = Finding("bm25", confidence: 0.90, competence: 0.30);

        Assert.Equal(ConceptVerdict.BlindSpot, finding.Verdict);

        Assert.Equal(0.60, finding.Overconfidence, tolerance: 1e-9);

        Assert.Contains("never go back and check", finding.Says, StringComparison.Ordinal);
    }

    [Fact]
    public void Sure_and_right_is_solid_and_needs_nothing()
    {
        Assert.Equal(ConceptVerdict.Solid, Finding("tokenisation", 0.90, 0.95).Verdict);
    }

    /// **They are better than they think** — and they are spending time revising something they
    /// already hold. That is *their time*, and PRAXIS exists to give it back (I-10).
    [Fact]
    public void Unsure_and_right_means_stop_revising_it()
    {
        var finding = Finding("hnsw", confidence: 0.30, competence: 0.90);

        Assert.Equal(ConceptVerdict.StrongerThanYouThink, finding.Verdict);

        Assert.True(finding.Overconfidence < 0);

        Assert.Contains("Stop revising it", finding.Says, StringComparison.Ordinal);
    }

    /// **Not knowing something, and knowing that you do not, is a completely healthy state.**
    ///
    /// It is where learning starts. A product that treats it as failure has taught its learners to
    /// hide it — which is how a blind spot gets made in the first place.
    [Fact]
    public void Not_knowing_and_admitting_it_is_not_a_failure()
    {
        var finding = Finding("hnsw", confidence: 0.20, competence: 0.20);

        Assert.Equal(ConceptVerdict.HonestGap, finding.Verdict);

        Assert.Contains("completely healthy", finding.Says, StringComparison.Ordinal);
        Assert.Contains("costs you nothing", finding.Says, StringComparison.Ordinal);
    }

    // ── The refusal ──────────────────────────────────────────────────────────

    /// **PRAXIS does not call somebody deluded on thin data.**
    ///
    /// Two wrong answers is a bad afternoon, not a blind spot.
    ///
    /// A tool that diagnoses overconfidence from two data points **has itself demonstrated
    /// overconfidence** — and it has done so while lecturing a human being about theirs.
    [Fact]
    public void PRAXIS_will_not_diagnose_a_blind_spot_from_a_bad_afternoon()
    {
        // Every bit as "sure and wrong" as a real blind spot — on two answers.
        var thin = Diagnosis.Assess(Guid.CreateVersion7(), "bm25", 0.95, 0.0, samples: 2);

        Assert.Equal(ConceptVerdict.TooEarlyToSay, thin.Verdict);

        Assert.NotEqual(ConceptVerdict.BlindSpot, thin.Verdict);

        Assert.Contains("not enough evidence", thin.Says, StringComparison.Ordinal);
        Assert.Contains("2 answers", thin.Says, StringComparison.Ordinal);
    }

    /// And it does not sneak into the headline list either.
    [Fact]
    public void A_thin_finding_never_reaches_the_blind_spot_list()
    {
        var report = Diagnosis.Report(
        [
            Diagnosis.Assess(Guid.CreateVersion7(), "thin", 0.99, 0.0, samples: 1),
            Finding("real", 0.90, 0.20),
        ]);

        Assert.Single(report.BlindSpots);
        Assert.Equal("real", report.BlindSpots[0].Slug);

        // And the refusal is **shown**, not quietly dropped — a diagnostic that hides its own
        // uncertainty is doing the exact thing it is accusing the learner of.
        Assert.Single(report.TooEarlyToSay);
        Assert.Equal("thin", report.TooEarlyToSay[0].Slug);
    }

    [Fact]
    public void The_evidence_floor_is_not_one_answer()
    {
        Assert.True(Diagnosis.MinimumEvidence >= 5);
    }

    // ── The screen ───────────────────────────────────────────────────────────

    /// **Ranked by danger, not by score.**
    ///
    /// The surest-and-wrongest comes first. A list sorted by score would put their worst subject at
    /// the top — and they already know what their worst subject is.
    [Fact]
    public void The_blind_spots_are_ranked_by_how_sure_they_were()
    {
        var report = Diagnosis.Report(
        [
            Finding("mildly-wrong", confidence: 0.75, competence: 0.60),   // gap 0.15
            Finding("certain-and-wrong", confidence: 0.99, competence: 0.10),   // gap 0.89
            Finding("fairly-wrong", confidence: 0.85, competence: 0.40),   // gap 0.45
        ]);

        Assert.Equal(
            ["certain-and-wrong", "fairly-wrong", "mildly-wrong"],
            report.BlindSpots.Select(f => f.Slug));
    }

    [Fact]
    public void The_headline_names_what_they_are_wrong_about()
    {
        var report = Diagnosis.Report([Finding("bm25", 0.95, 0.20)]);

        Assert.True(report.FoundABlindSpot);

        Assert.Contains("bm25", report.Headline, StringComparison.Ordinal);
        Assert.Contains("never have gone back to check", report.Headline, StringComparison.Ordinal);
    }

    /// **Finding nothing is a real result, and it is worth more than a high score.**
    [Fact]
    public void Finding_no_blind_spot_is_said_plainly_and_not_dressed_up()
    {
        var report = Diagnosis.Report([Finding("tokenisation", 0.9, 0.95), Finding("bm25", 0.2, 0.1)]);

        Assert.False(report.FoundABlindSpot);

        Assert.Contains("did not find anything", report.Headline, StringComparison.Ordinal);
        Assert.Contains("worth more than a high score", report.Headline, StringComparison.Ordinal);
    }

    // ── And what it is not ───────────────────────────────────────────────────

    /// **There is no score on this screen, and there must never be one.**
    ///
    /// No grade, no percentage, no percentile. This exists to tell a learner where to **look** — and
    /// the moment a number appears, it becomes the thing they look at instead. Every learner who has
    /// ever been given a mark knows exactly how fast the rest of the feedback stops being read.
    [Fact]
    public void The_result_screen_has_no_score_on_it()
    {
        var fields = typeof(DiagnosticReport)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "Score", "Grade", "Percentage", "Percentile", "Rank", "Total", "Points", "Mark", "Level",
        ];

        foreach (var name in forbidden)
        {
            Assert.False(
                fields.Contains(name),
                $"DiagnosticReport.{name} exists. A number has appeared on the one screen whose "
                + "whole job is to make a learner read the words — and it is now the only thing "
                + "they will read.");
        }
    }

    /// **It names the concept, not the person.**
    ///
    /// *"You said you were sure about BM25, and you were right three times in ten"* is a fact about
    /// BM25 and about a Tuesday. *"You are overconfident"* is a verdict on a human being, and PRAXIS
    /// is not entitled to one.
    [Fact]
    public void It_says_something_about_a_concept_and_never_about_the_person()
    {
        foreach (var finding in new[]
        {
            Finding("bm25", 0.95, 0.10),
            Finding("hnsw", 0.20, 0.90),
            Finding("tokenisation", 0.90, 0.95),
            Finding("chunking", 0.10, 0.10),
        })
        {
            Assert.Contains(finding.Slug, finding.Says, StringComparison.Ordinal);

            foreach (var verdict in new[] { "you are overconfident", "you are arrogant", "you are bad" })
            {
                Assert.DoesNotContain(verdict, finding.Says, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ── The boundary ─────────────────────────────────────────────────────────

    [Fact]
    public void Confidence_is_clamped_because_nobody_is_110_percent_sure()
    {
        var finding = Diagnosis.Assess(Guid.CreateVersion7(), "bm25", 1.5, -0.2, 10);

        Assert.Equal(1.0, finding.StatedConfidence);
        Assert.Equal(0.0, finding.DemonstratedCompetence);
    }

    [Fact]
    public void A_finding_about_nothing_is_a_bug()
    {
        Assert.Throws<ArgumentException>(
            () => Diagnosis.Assess(Guid.CreateVersion7(), "  ", 0.5, 0.5, 10));
    }
}
