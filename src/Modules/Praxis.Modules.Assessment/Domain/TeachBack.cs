using System.Text.RegularExpressions;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Domain;

/// **Explain it out loud, to somebody who has never heard of it.**
///
/// The Feynman test. It is the most revealing thing a learner can do and the most dangerous thing to
/// grade, because *speech carries a dozen signals that have nothing whatever to do with knowing the
/// thing* — and a naive grader will happily score every one of them.
///
/// ## The three ways this goes wrong, and what PRAXIS does instead
///
/// ### 1. A stammer is not a knowledge gap
///
/// A learner who says *"so — so the, um, the index, right, the inverted index — it maps terms to,
/// er, to documents"* has said exactly the same thing as one who says it smoothly. **They have
/// demonstrated identical knowledge.** One of them is nervous, or thinking, or has a stutter, or is
/// speaking their third language.
///
/// A grader that scores fluency is scoring **confidence, class, and first language**, and calling it
/// understanding. So disfluency is **stripped before the rubric ever sees the text** — not weighted,
/// not discounted: *removed*, so that the judge is physically unable to react to it.
///
/// ### 2. Hedging is a calibration signal, not a penalty
///
/// *"I think it's maybe because the term frequency saturates?"*
///
/// The instinct is to mark that down — it sounds unsure. **That instinct is exactly backwards.** A
/// learner who is unsure and says so is doing the single most valuable thing in this product (C-6):
/// they are telling you their confidence, honestly, and PRAXIS can measure whether they were right
/// to be unsure.
///
/// **Penalise hedging and you teach people to sound certain.** That is the worst lesson this system
/// could possibly teach, and it is the one every viva, every interview, and every classroom in the
/// world teaches by accident. So hedges are counted, routed to **calibration**, and **never touched
/// by the rubric.**
///
/// ### 3. We do not grade a bad microphone
///
/// If the transcript is noise, PRAXIS **abstains**. It does not do its best with a mangled
/// transcript and write the result into a model of somebody's mind. The learner is asked to try
/// again, and nothing is recorded against them — because what failed was **our capture**, not their
/// understanding.
///
/// ## And when it is graded well, it still cannot confer mastery
///
/// `EXPLAIN` is ceiling-bound (C-4). A learner can give the most beautiful explanation ever recorded
/// and **it will not, alone, make them held** — because a language model graded it, and a language
/// model's opinion is exactly what the Evidence Ceiling refuses to trust. What it *can* do is raise
/// belief, sharpen the Twin, and reveal misconceptions. That is a great deal. It is not mastery.
public static class TeachBack
{
    /// Below this ASR confidence, PRAXIS will not grade the transcript.
    ///
    /// **What failed was our capture, not their understanding**, and grading noise would write a
    /// false belief into a model of a person's mind on the strength of a bad microphone.
    public const double MinimumTranscriptionConfidence = 0.70;

    /// Filled pauses and verbal tics. **Stripped, not weighted** — along with the comma the
    /// transcriber hung off them, so that *"the index, uh, maps terms"* becomes *"the index maps
    /// terms"* and not *"the index, maps terms"*.
    ///
    /// **What is deliberately NOT in this list:** discourse markers — *so*, *right*, *well*, *okay*,
    /// *basically*. They *look* like filler and they are not: *"so the tenth one counts less"* is a
    /// causal *so*, and *"the right answer"* is a real word. A stripper that ate them would **damage
    /// the content**, and damaging the content is a far worse failure than leaving a marker in, since
    /// it would mark a learner down for something they actually said. When in doubt, PRAXIS keeps the
    /// word.
    private static readonly Regex Disfluency = new(
        @"[,]?\s*\b(um+|uh+|er+|erm+|ah+|hmm+|mm+|like|you know|I mean|sort of|kind of)\b[,]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// A word repeated immediately — *"the the index"* — and false starts — *"so — so the index"*.
    private static readonly Regex Stutter = new(
        @"\b(\w+)([,\s—-]+\1\b)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// Is there a single actual word left, once the speech has been taken out of it?
    private static readonly Regex AnyWord = new(@"\w", RegexOptions.Compiled);

    /// **Honest uncertainty.** Counted, routed to calibration, and never seen by the rubric.
    private static readonly Regex Hedge = new(
        @"\b(I think|I guess|maybe|perhaps|possibly|probably|not sure|might be|could be|I believe)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// Prepare a spoken explanation for grading.
    ///
    /// **Refuses** when the capture failed. Otherwise it returns text the rubric can grade *and only
    /// what the rubric should grade* — the content, with the speech stripped out of it.
    public static Result<GradableExplanation> Prepare(Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        if (transcript.Confidence < MinimumTranscriptionConfidence)
        {
            // **We do not grade a bad microphone.**
            return Result<GradableExplanation>.Invalid(
                "PRAXIS could not hear that clearly enough to mark it, and it is not going to guess "
                + "at what you said and then write the guess into a model of your mind. Nothing has "
                + "been recorded against you — what failed was our recording, not your "
                + "understanding. Try it again.");
        }

        var raw = transcript.Text?.Trim() ?? string.Empty;

        if (raw.Length == 0)
        {
            return Result<GradableExplanation>.Invalid("There is nothing here to mark.");
        }

        var hedges = Hedge.Count(raw);

        // **The order matters.** Hedges are counted from the *raw* text — before anything is removed
        // — because they are signal. Everything below removes noise.
        var stripped = Disfluency.Replace(raw, " ");
        stripped = Stutter.Replace(stripped, "$1");
        stripped = Regex.Replace(stripped, @"\s+([,.;:?!])", "$1");
        stripped = Regex.Replace(stripped, @"\s{2,}", " ").Trim();

        var disfluencies = CountWords(raw) - CountWords(stripped);

        if (!AnyWord.IsMatch(stripped))
        {
            return Result<GradableExplanation>.Invalid(
                "Once the filler was removed there was nothing left to mark. That is not a "
                + "criticism — it means you have not said anything about the idea yet.");
        }

        return Result<GradableExplanation>.Success(new GradableExplanation(
            Content: stripped,
            Hedges: hedges,
            DisfluenciesRemoved: Math.Max(0, disfluencies),
            Confidence: transcript.Confidence));
    }

    private static int CountWords(string text)
        => text.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
}

/// What came out of the microphone.
public sealed record Transcript(
    string Text,

    /// The ASR's own confidence in what it heard, in [0, 1]. **Below the floor, PRAXIS abstains** —
    /// it does not do its best with a mangled transcript.
    double Confidence);

/// **What the rubric is allowed to see** — and, just as importantly, what it is not.
///
/// The judge gets `Content`. It never gets the hesitation, the false starts, the stammer, or the
/// accent — because none of those are the thing being measured, and a judge that can see them will
/// react to them.
public sealed record GradableExplanation(
    /// The content, with the speech taken out of it. **This is all the rubric ever sees.**
    string Content,

    /// **Honest uncertainty, and it is worth something.** It goes to calibration (C-6), and it must
    /// never touch the score — because a system that penalises hedging teaches people to sound
    /// certain, which is the worst lesson it could possibly teach.
    int Hedges,

    /// How much speech was stripped. Recorded for the audit trail; **it is not a number about the
    /// learner** and nothing is allowed to act on it.
    int DisfluenciesRemoved,

    double Confidence)
{
    /// The confidence the learner *expressed*, inferred from how much they hedged.
    ///
    /// This is fed to `Calibration` (M3.4), which will find out whether they were **right to be
    /// unsure** — which is the question worth asking, and the opposite of punishing them for asking
    /// it of themselves.
    public double ExpressedConfidence
        => Math.Clamp(1.0 - (Hedges * 0.15), 0.1, 1.0);
}

/// **The Evidence Ceiling, at the point where it is most tempting to forget it** (C-4).
///
/// A learner has just given a genuinely brilliant explanation. Everything in the product wants to
/// reward it. **It cannot make them held**, because a language model graded it — and the whole
/// architecture rests on a model's opinion never being sufficient.
public static class TeachBackCredit
{
    /// What an explanation is worth, and what it can never be worth.
    public static Result<double> Credit(double rubricScore, MasteryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (rubricScore is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rubricScore), rubricScore, "A score is 0–1.");
        }

        var clause = contract.Clauses.FirstOrDefault(c => c.Type == EvidenceType.Explain);

        if (clause is null)
        {
            return Result<double>.Invalid(
                "This concept's contract does not accept EXPLAIN evidence at all. Some things cannot "
                + "be talked about into existence, and the person who wrote the contract said so.");
        }

        if (rubricScore < clause.MinScore)
        {
            return Result<double>.Success(0.0);
        }

        // Capped by the contract, and the contract itself was constructed so that model-graded
        // evidence **cannot sum to the hold threshold** (the Evidence Ceiling, enforced in
        // `PoMContract`). So no arithmetic here can ever produce mastery, however good the
        // explanation was.
        return Result<double>.Success(Math.Min(rubricScore, clause.MaxContribution));
    }
}
