using Praxis.Shared;

namespace Praxis.Modules.Twin.Domain;

/// **What PRAXIS believes about one person and one concept.**
///
/// This is the most intimate row in the product. It says how likely you are to know something, how
/// fast that belief is rotting, how well you can predict your own correctness, and which specific
/// wrong idea you are carrying. It is yours (CANON §5.9).
///
/// It is a **projection**. Nothing here is authored, nothing here is set by hand, and nothing here
/// survives a rebuild that the event log does not justify. If you find yourself wanting to write a
/// value into this row for any reason other than "an event said so", the kernel is being broken.
///
/// Mastery is **never binary and never permanent**. You do not complete a concept — you *hold* it,
/// and it *bleeds* unless refreshed (I-2).
public sealed class ConceptState : BaseEntity
{
    public Guid LearnerId { get; private set; }
    public Guid ConceptId { get; private set; }

    // ── What we believe ──────────────────────────────────────────────────────

    /// P(knows) — the probability this learner would demonstrate the concept *right now*, at the
    /// standard its contract demands.
    ///
    /// This is belief, not a score. It rises only on evidence (M3.2), and it decays with time
    /// (M3.3) whether or not anyone is watching.
    public double PKnows { get; private set; }

    /// The mastery credit accrued from accepted evidence, before decay is applied. This is what the
    /// Evidence Ceiling caps (M3.5): model-graded evidence can raise it, but never past the point
    /// where the concept would count as held.
    public double MasteryCredit { get; private set; }

    /// Whether the concept is currently **held** — `MasteryCredit` (after decay) is at or above the
    /// contract's threshold. Not a flag anyone can set. A verb, not a badge.
    public bool IsHeld { get; private set; }

    // ── How fast it is rotting ───────────────────────────────────────────────

    /// FSRS stability: the number of days until retrievability falls to 90%. Higher means the
    /// memory holds longer. It grows with successful retrieval and collapses on failure.
    public double Stability { get; private set; }

    /// FSRS difficulty (1–10): how hard *this learner* finds *this concept*, as revealed by their
    /// own history. Not the concept's difficulty prior — that is the graph's opinion; this is the
    /// learner's reality.
    public double Difficulty { get; private set; }

    /// When they last produced evidence. Decay is computed from here, so a delayed write must never
    /// make a concept look fresher than it is.
    public DateTime? LastEvidenceAt { get; private set; }

    /// When retrievability is forecast to fall below threshold. The rep queue reads this.
    public DateTime? NextDueAt { get; private set; }

    // ── The Dunning–Kruger delta ─────────────────────────────────────────────

    /// The mean confidence this learner *stated* before being graded on this concept.
    public double MeanStatedConfidence { get; private set; }

    /// The proportion of those they actually got right.
    public double MeanCorrectness { get; private set; }

    /// **Confidence minus competence.** Positive means they think they know it better than they do
    /// — the most dangerous state a learner can be in, because they will not study it.
    ///
    /// Negative means they are better than they believe, and are wasting time on something they
    /// already hold. Both are worth telling them.
    public double ConfidenceGap => MeanStatedConfidence - MeanCorrectness;

    /// Brier score: mean squared error between stated confidence and outcome. **Lower is better**;
    /// 0.25 is what you get by always saying "50%". First-class, sitting next to mastery — and the
    /// reason cheating is measurable without being blocked (C-6): a learner who outsources their
    /// answers gets good scores and catastrophic calibration, because they never produced them.
    public double BrierScore { get; private set; }

    /// How many graded items with a stated confidence have contributed. Calibration on n=1 is
    /// noise, and the UI must not report it as though it were knowledge.
    public int CalibrationSamples { get; private set; }

    // ── How much we trust what we believe ────────────────────────────────────

    /// How much of this belief rests on evidence a model could have fabricated.
    ///
    /// 1.0 — everything came from a build that ran, a never-seen problem, or a prediction reality
    ///       settled.
    /// Low — it is mostly a language model's opinion of an explanation.
    ///
    /// Recorded honestly and exported into the Ledger honestly (C-6). We do not need to *block*
    /// cheating. We need to *measure* it, discount it, and say so.
    public double Assurance { get; private set; }

    public int EvidenceCount { get; private set; }

    private ConceptState() { }   // EF

    /// A learner who has never touched a concept. Not "knows nothing" — *unknown*, which is a
    /// different claim and the honest one on day zero.
    public static ConceptState Unseen(Guid learnerId, Guid conceptId) => new()
    {
        LearnerId = learnerId,
        ConceptId = conceptId,

        PKnows = PriorPKnows,
        MasteryCredit = 0.0,
        IsHeld = false,

        Stability = 0.0,
        Difficulty = DefaultDifficulty,

        MeanStatedConfidence = 0.0,
        MeanCorrectness = 0.0,
        BrierScore = 0.0,
        CalibrationSamples = 0,

        Assurance = 0.0,
        EvidenceCount = 0,
    };

    /// The BKT prior: what we assume before any evidence at all. Deliberately low — PRAXIS starts
    /// by believing you know nothing, and makes you show it otherwise.
    public const double PriorPKnows = 0.10;

    /// FSRS difficulty starts mid-scale and is pulled by the learner's own outcomes.
    public const double DefaultDifficulty = 5.0;

    /// Used by the projection (M3.2 onwards). Everything it writes must be a pure function of the
    /// events replayed so far — no clock reads, no model calls, no randomness. If a rebuild of the
    /// same log produces a different Twin, the Twin is not a projection and the Ledger is not
    /// verifiable.
    internal void Set(
        double pKnows,
        double masteryCredit,
        bool isHeld,
        double stability,
        double difficulty,
        DateTime? lastEvidenceAt,
        DateTime? nextDueAt,
        double meanStatedConfidence,
        double meanCorrectness,
        double brierScore,
        int calibrationSamples,
        double assurance,
        int evidenceCount)
    {
        PKnows = Clamp01(pKnows);
        MasteryCredit = Clamp01(masteryCredit);
        IsHeld = isHeld;
        Stability = Math.Max(0.0, stability);
        Difficulty = Math.Clamp(difficulty, 1.0, 10.0);
        LastEvidenceAt = lastEvidenceAt;
        NextDueAt = nextDueAt;
        MeanStatedConfidence = Clamp01(meanStatedConfidence);
        MeanCorrectness = Clamp01(meanCorrectness);
        BrierScore = Math.Clamp(brierScore, 0.0, 1.0);
        CalibrationSamples = Math.Max(0, calibrationSamples);
        Assurance = Clamp01(assurance);
        EvidenceCount = Math.Max(0, evidenceCount);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
