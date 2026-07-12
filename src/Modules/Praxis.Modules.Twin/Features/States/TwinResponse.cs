namespace Praxis.Modules.Twin.Features.States;

/// What PRAXIS believes about one learner and one concept, **as of right now**.
///
/// Decay is applied when this is read, not by a nightly job. Knowledge rots continuously and the
/// learner is entitled to see it rotting — refresh the page an hour later and the number is lower.
/// A belief that only decays when a cron job remembers to run is not a model of forgetting; it is
/// a model of cron.
public sealed record ConceptStateResponse(
    Guid ConceptId,

    /// The probability they could demonstrate this **right now**, after decay.
    double PKnows,

    /// What it was when they last produced evidence. The distance between the two is what they have
    /// lost since.
    double PKnowsAtLastEvidence,

    /// Mastery credit from accepted evidence. Only this can cross the hold threshold.
    double MasteryCredit,

    bool IsHeld,

    /// How available it is right now, on the forgetting curve. 1.0 the moment they proved it.
    double Retrievability,

    /// Days until it falls below the review threshold. Negative means it already has.
    ///
    /// **Null when nothing was ever proved.** There is no due date for a concept the learner has
    /// never held — and saying "infinity" would be both a lie and unserialisable.
    double? DaysUntilDue,

    DateTime? LastEvidenceAt,
    DateTime? NextDueAt,

    /// **Confidence minus competence.** Positive: they think they are better than they are, and
    /// will not study it. Negative: they hold it and do not know they hold it, and are wasting time.
    double ConfidenceGap,

    /// Lower is better. 0.25 is what you get by saying "50%" about everything.
    double BrierScore,

    /// Null until there is enough to say anything. Reporting calibration on n=2 would be exactly
    /// the overconfidence the metric exists to catch.
    bool CalibrationIsMeaningful,

    /// How much of this belief rests on work a language model could not have faked. 1.0 means all
    /// of it came from a build that ran, a never-seen problem, or a prediction reality settled.
    double Assurance,

    int EvidenceCount,

    IReadOnlyList<MisconceptionResponse> ActiveMisconceptions);

/// A named wrong belief the learner is carrying. Stated in the first person, because a
/// misconception they do not recognise as theirs is one they keep.
public sealed record MisconceptionResponse(
    string Key,
    string Statement,
    int TimesObserved,

    /// They were over this, and it came back. The tutor needs to know.
    bool HasRelapsed);

/// The learner's own mind, summarised — and willing to tell them the truth about it.
public sealed record TwinSummaryResponse(
    int ConceptsHeld,
    int ConceptsInProgress,

    /// **Held, but rotting.** Above the threshold today, below it soon. This is the number nobody
    /// else in the industry shows you.
    int ConceptsDecaying,

    /// Already fallen below. They think they know these.
    int ConceptsLapsed,

    /// Across everything they have been graded on. Lower is better.
    double OverallBrierScore,

    /// Positive means systematic overconfidence across the whole domain.
    double OverallConfidenceGap,

    bool CalibrationIsMeaningful,

    int ActiveMisconceptions,

    /// What is about to rot, soonest first. The rep queue, in effect.
    IReadOnlyList<DecayForecastEntry> DecayForecast);

public sealed record DecayForecastEntry(
    Guid ConceptId,
    double PKnows,
    double Retrievability,
    double DaysUntilDue,
    bool AlreadyLapsed);
