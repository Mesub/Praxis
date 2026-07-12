namespace Praxis.Modules.Assessment.Domain;

/// **"Here is what you think you know, and don't."**
///
/// Twelve minutes, and PRAXIS can hand a learner something no course, no quiz and no certificate
/// has ever handed them: **a list of the things they are confidently wrong about.**
///
/// ## Why this is the moment the product earns its existence
///
/// Being wrong is survivable. Everybody is wrong about most things, most of the time, and they know
/// it — and the things you *know* you do not know are harmless, because you look them up, you ask,
/// you go carefully.
///
/// **The dangerous ones are the things you are sure about and wrong about.** You never look those
/// up. You never ask about those. You build on them, and everything you build on them is subtly
/// crooked, and when it eventually falls over you will blame the wrong thing — because the beam you
/// trusted was never the one you were checking.
///
/// That is the list this produces. It is the only list in the product that a learner could not have
/// written for themselves, by definition: **you cannot list your own blind spots.**
///
/// ## The two rules that stop this being cruel
///
/// 1. **PRAXIS never calls somebody deluded on thin data.** Two wrong answers is a bad afternoon,
///    not a blind spot. Below the sample floor, the verdict is *"too early to say"* — and it says
///    that, out loud, rather than quietly producing a confident finding from noise. **A tool that
///    diagnoses overconfidence on two data points has itself demonstrated overconfidence**, and it
///    has done so while lecturing somebody about theirs.
///
/// 2. **It names the concept, not the person.** *"You said you were sure about BM25, and you were
///    right three times in ten"* is a fact about BM25 and about a Tuesday. *"You are overconfident"*
///    is a verdict about a human being, and PRAXIS is not entitled to it.
///
/// ## And what it is not
///
/// There is **no score**. No grade, no percentage, no "you are in the 62nd percentile". This screen
/// exists to tell a learner where to *look*, and a number would immediately become the thing they
/// look at instead. A test asserts the absence.
public static class Diagnosis
{
    /// Above this stated confidence, the learner is telling you they are **sure**. That is the word
    /// that makes a wrong answer dangerous.
    public const double ConfidentAt = 0.70;

    /// Below this demonstrated competence, they are not, in fact, right.
    public const double CompetentAt = 0.70;

    /// **The floor beneath which PRAXIS says nothing.**
    ///
    /// Two wrong answers is a bad afternoon. Diagnosing a blind spot from it would be a tool
    /// demonstrating overconfidence *while lecturing a human being about theirs*.
    public const int MinimumEvidence = 5;

    public static ConceptFinding Assess(
        Guid conceptId,
        string slug,
        double statedConfidence,
        double demonstratedCompetence,
        int samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var verdict = Verdict(statedConfidence, demonstratedCompetence, samples);

        return new ConceptFinding(
            conceptId,
            slug,
            Math.Clamp(statedConfidence, 0.0, 1.0),
            Math.Clamp(demonstratedCompetence, 0.0, 1.0),
            samples,
            verdict);
    }

    private static ConceptVerdict Verdict(double confidence, double competence, int samples)
    {
        // **The refusal.** Not enough evidence to say anything about anybody.
        if (samples < MinimumEvidence)
        {
            return ConceptVerdict.TooEarlyToSay;
        }

        var sure = confidence >= ConfidentAt;
        var able = competence >= CompetentAt;

        return (sure, able) switch
        {
            // **The list that matters.** Sure, and wrong. They will not study this, because they do
            // not know it is broken — and everything they build on it will be subtly crooked.
            (true, false) => ConceptVerdict.BlindSpot,

            // They know it, and they know they know it. Nothing to do here.
            (true, true) => ConceptVerdict.Solid,

            // **They are better than they think**, and they are spending time revising something
            // they already hold. That is *their time*, and PRAXIS is meant to give it back (I-10).
            (false, true) => ConceptVerdict.StrongerThanYouThink,

            // They do not know it, and they know they do not. **This is a completely healthy state**
            // — it is where learning starts, and PRAXIS does not treat it as a failure.
            (false, false) => ConceptVerdict.HonestGap,
        };
    }

    /// **The screen.** Ranked by danger, not by score.
    ///
    /// The blind spots come first — not because they are the worst results, but because they are the
    /// ones the learner would never have found on their own. A list sorted by score would put their
    /// worst subject at the top, and they already know what their worst subject is.
    public static DiagnosticReport Report(IReadOnlyList<ConceptFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var blindSpots = findings
            .Where(f => f.Verdict is ConceptVerdict.BlindSpot)
            .OrderByDescending(f => f.Overconfidence)   // the surest and the most wrong, first
            .ThenBy(f => f.Slug, StringComparer.Ordinal)
            .ToList();

        var stronger = findings
            .Where(f => f.Verdict is ConceptVerdict.StrongerThanYouThink)
            .OrderBy(f => f.StatedConfidence)
            .ThenBy(f => f.Slug, StringComparer.Ordinal)
            .ToList();

        return new DiagnosticReport(
            BlindSpots: blindSpots,
            StrongerThanYouThink: stronger,
            Solid: [.. findings.Where(f => f.Verdict is ConceptVerdict.Solid)],
            HonestGaps: [.. findings.Where(f => f.Verdict is ConceptVerdict.HonestGap)],
            TooEarlyToSay: [.. findings.Where(f => f.Verdict is ConceptVerdict.TooEarlyToSay)]);
    }
}

/// One concept, and the gap between what the learner **said** and what they **did**.
public sealed record ConceptFinding(
    Guid ConceptId,
    string Slug,

    /// What they told PRAXIS, *before* they saw the outcome. That ordering is the whole point (C-6):
    /// a confidence stated afterwards is a memory, not a prediction.
    double StatedConfidence,

    /// What they actually managed.
    double DemonstratedCompetence,

    int Samples,

    ConceptVerdict Verdict)
{
    /// **The Dunning–Kruger delta**: what they said, minus what they did.
    ///
    /// Positive is the dangerous direction. Negative means they are underselling themselves, which
    /// costs them time rather than correctness — and is still worth telling them.
    public double Overconfidence => StatedConfidence - DemonstratedCompetence;

    /// The sentence a human should read. It is about the **concept and a Tuesday**, never about the
    /// person: *"you are overconfident"* is a verdict on a human being, and PRAXIS is not entitled
    /// to one.
    public string Says => Verdict switch
    {
        ConceptVerdict.BlindSpot =>
            $"You said you were sure about {Slug}. You were right "
            + $"{DemonstratedCompetence:P0} of the time. **This is the kind of thing you never go "
            + "back and check** — and everything you build on it inherits the crack.",

        ConceptVerdict.StrongerThanYouThink =>
            $"You hold {Slug} better than you think you do ({DemonstratedCompetence:P0} against "
            + $"{StatedConfidence:P0} confidence). Stop revising it. That time is yours.",

        ConceptVerdict.Solid =>
            $"{Slug}: you know it, and you know that you know it. Nothing to do.",

        ConceptVerdict.HonestGap =>
            $"{Slug}: you do not know this, and you knew that. **That is a completely healthy "
            + "place to be** — it is where learning starts, and it costs you nothing.",

        ConceptVerdict.TooEarlyToSay =>
            $"{Slug}: not enough evidence yet. PRAXIS is not going to tell you something about "
            + "your own mind on the strength of "
            + $"{Samples} answer{(Samples == 1 ? string.Empty : "s")}.",

        _ => Slug,
    };
}

public enum ConceptVerdict
{
    /// **Sure, and wrong.** The only entry on this list a learner could not have written for
    /// themselves — by definition, because *you cannot list your own blind spots*.
    BlindSpot = 1,

    /// **Better than they believe.** They are revising something they already hold, and that is
    /// their time being spent on nothing.
    StrongerThanYouThink = 2,

    /// They know it, and they know they know it.
    Solid = 3,

    /// They do not know it, and they knew that. **A completely healthy state**, and PRAXIS does not
    /// treat it as failure — it is where learning starts.
    HonestGap = 4,

    /// **PRAXIS declines to say.** Two wrong answers is a bad afternoon, not a blind spot.
    TooEarlyToSay = 5,
}

/// **The screen.** Note what is not on it: a score.
///
/// No grade, no percentage, no percentile. This exists to tell a learner where to *look* — and a
/// number would instantly become the thing they looked at instead.
public sealed record DiagnosticReport(
    /// **The headline, and the reason the product exists.** Ranked by how sure they were, not by how
    /// badly they did.
    IReadOnlyList<ConceptFinding> BlindSpots,

    /// The good news that is also actionable: stop revising these.
    IReadOnlyList<ConceptFinding> StrongerThanYouThink,

    IReadOnlyList<ConceptFinding> Solid,

    /// Not a failure list. A starting line.
    IReadOnlyList<ConceptFinding> HonestGaps,

    /// **The findings PRAXIS refuses to make.** Shown as such, rather than quietly dropped — because
    /// a diagnostic that hides its own uncertainty is doing the exact thing it is accusing the
    /// learner of.
    IReadOnlyList<ConceptFinding> TooEarlyToSay)
{
    public bool FoundABlindSpot => BlindSpots.Count > 0;

    /// What PRAXIS leads with. **Never a score.**
    public string Headline => BlindSpots.Count switch
    {
        0 =>
            "PRAXIS did not find anything you are confidently wrong about. That is rarer than it "
            + "sounds, and it is worth more than a high score.",

        1 =>
            $"There is one thing you are sure about and wrong about: **{BlindSpots[0].Slug}**. It is "
            + "the kind of thing you would never have gone back to check.",

        _ =>
            $"There are {BlindSpots.Count} things you are sure about and wrong about. They are the "
            + "ones you would never have gone back to check — so PRAXIS is starting there.",
    };
}
