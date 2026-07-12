namespace Praxis.Modules.Twin.Learning;

/// **What is wrong with this learner, by name.**
///
/// A quiz app knows your score. A Twin knows that you believe *cosine similarity is unaffected by
/// whether you normalised the vectors* — which is a claim you can teach against, generate an item
/// against, and watch die.
///
/// ## The rules, and why each one is what it is
///
/// **A misconception is not retired by being told.** Explaining to someone that they are wrong is
/// the single least reliable way of finding out whether they still are. It is retired when they
/// stop *answering* as though they hold it — the same standard the rest of the product applies to
/// everything else. Being taught is not evidence.
///
/// **One contradiction is not enough.** Once could be luck, or a lucky guess, or the phrasing of
/// one item. A Twin that forgives too easily is a Twin that keeps telling a learner they are fine.
///
/// **A retired misconception can come back.** Misconceptions relapse — under time pressure, under
/// fatigue, on a variant of the problem that does not look like the one they were corrected on.
/// Pretending otherwise is how a person ships the same bug twice.
///
/// **Contradictions do not accumulate across relapses.** If they show it again, the count resets:
/// they have to earn their way out again from scratch. Otherwise a learner who relapses six times
/// and is contradicted once each time would be recorded as cured.
///
/// Pure functions over an immutable state, so replaying the log reproduces the same beliefs.
public static class MisconceptionSet
{
    /// It takes this many clean contradictions to retire one. Once could be luck.
    public const int ContradictionsToRetire = 2;

    /// The learner answered in a way consistent with holding this wrong belief.
    public static MisconceptionState Observe(MisconceptionState state, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state with
        {
            TimesObserved = state.TimesObserved + 1,
            LastObservedAt = at,
            FirstObservedAt = state.FirstObservedAt ?? at,

            // Showing it again resets the road out. They do not get to keep credit earned before
            // the relapse — a learner contradicted once, then relapsing, then contradicted once
            // again, has not demonstrated anything except inconsistency.
            TimesContradicted = 0,

            IsActive = true,
        };
    }

    /// The learner answered in a way this wrong belief cannot explain.
    public static MisconceptionState Contradict(MisconceptionState state, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsActive)
        {
            // Already retired. Contradicting it again is not evidence of anything new.
            return state;
        }

        var contradictions = state.TimesContradicted + 1;

        return state with
        {
            TimesContradicted = contradictions,
            LastContradictedAt = at,
            IsActive = contradictions < ContradictionsToRetire,
            RetiredAt = contradictions >= ContradictionsToRetire ? at : null,
        };
    }

    /// **Being taught changes nothing.**
    ///
    /// This function exists so that the intent is impossible to miss when someone goes looking for
    /// where instruction retires a misconception. It does not, and there is a test that says so.
    public static MisconceptionState Taught(MisconceptionState state) => state;

    /// How much this wrong belief should suppress our confidence in the concept it sits under.
    ///
    /// A learner who holds an active misconception about a concept does not know that concept,
    /// however well they score around it — because the moment the problem is phrased in the way
    /// that triggers it, they will be wrong, and confidently.
    public static double BeliefPenalty(MisconceptionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsActive)
        {
            return 0.0;
        }

        // Repeatedly demonstrated is worse than seen once. Caps out — beyond a point it is simply
        // "they hold this", and there is nothing more to learn from the fifth observation.
        var severity = Math.Min(1.0, state.TimesObserved / 3.0);

        return MaxPenalty * severity;
    }

    /// The most an active misconception can suppress belief in its concept. Deliberately large:
    /// a person carrying a confidently-held wrong model is *worse off* than a person who knows they
    /// do not know, because they will not ask.
    public const double MaxPenalty = 0.35;
}

/// One named wrong belief held by one learner about one concept.
public sealed record MisconceptionState(
    string Key,
    string Statement,
    int TimesObserved = 0,
    int TimesContradicted = 0,
    bool IsActive = false,
    DateTime? FirstObservedAt = null,
    DateTime? LastObservedAt = null,
    DateTime? LastContradictedAt = null,
    DateTime? RetiredAt = null)
{
    public static MisconceptionState Unseen(string key, string statement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);

        return new MisconceptionState(key.Trim().ToLowerInvariant(), statement.Trim());
    }

    /// How many times they have shown it *since* the last time we thought they were over it. This
    /// is what "relapse" means, and it is what the tutor should be told about.
    public bool HasRelapsed => RetiredAt is not null && IsActive;
}
