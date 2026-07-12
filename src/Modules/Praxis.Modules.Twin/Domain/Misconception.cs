using Praxis.Shared;

namespace Praxis.Modules.Twin.Domain;

/// A **named, specific wrong model** this learner holds.
///
/// Not "they got question 7 wrong". *"They believe cosine similarity is unaffected by whether the
/// vectors were normalised."* That is a claim you can teach against, generate an item against, and
/// watch die.
///
/// This is the difference between a quiz app and a Twin. A quiz app knows your score. This knows
/// what is wrong with you, by name (I-6).
public sealed class Misconception : BaseEntity
{
    public Guid LearnerId { get; private set; }
    public Guid ConceptId { get; private set; }

    /// The stable identifier from the pack's misconception library, so that the same wrong idea in
    /// two learners is the *same* wrong idea — that is what makes it minable at cohort scale.
    public string Key { get; private set; } = null!;

    /// The wrong belief, stated as the learner would state it. First person, on purpose: it has to
    /// be recognisable to them, or they will not believe they hold it.
    public string Statement { get; private set; } = null!;

    /// How many times their answers have been consistent with holding it.
    public int TimesObserved { get; private set; }

    /// How many times they have since answered in a way that contradicts it.
    public int TimesContradicted { get; private set; }

    public DateTime FirstObservedAt { get; private set; }
    public DateTime LastObservedAt { get; private set; }

    /// Whether they still appear to hold it.
    ///
    /// A misconception is not "fixed" because it was explained to them. It is fixed when they stop
    /// answering as though they hold it — which is the same standard the rest of the product
    /// applies to everything else.
    public bool IsActive { get; private set; } = true;

    private Misconception() { }   // EF

    public static Misconception Observed(
        Guid learnerId,
        Guid conceptId,
        string key,
        string statement,
        DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);

        return new Misconception
        {
            LearnerId = learnerId,
            ConceptId = conceptId,
            Key = key.Trim().ToLowerInvariant(),
            Statement = statement.Trim(),
            TimesObserved = 1,
            TimesContradicted = 0,
            FirstObservedAt = at,
            LastObservedAt = at,
            IsActive = true,
        };
    }

    internal void ObserveAgain(DateTime at)
    {
        TimesObserved++;
        LastObservedAt = at;
        IsActive = true;
    }

    /// They answered in a way this misconception cannot explain.
    ///
    /// It takes two clean contradictions to retire one. Once could be luck, and a Twin that
    /// forgives too easily is a Twin that keeps telling the learner they are fine.
    internal void Contradict()
    {
        TimesContradicted++;

        if (TimesContradicted >= ContradictionsToRetire)
        {
            IsActive = false;
        }
    }

    public const int ContradictionsToRetire = 2;

    internal void Reset()
    {
        TimesObserved = 0;
        TimesContradicted = 0;
        IsActive = false;
    }
}
