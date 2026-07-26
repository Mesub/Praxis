namespace Praxis.Modules.Tutor;

/// What the tutor may know about what the learner is looking at.
///
/// A port, like `IMisconceptionLibrary`: Tutor names what it needs, the composition root decides
/// where it comes from, and no module reads another module's tables. Until M17 the Tutor had no
/// such seam at all, which is why both hint paths sent a raw GUID as the "concept summary" and the
/// literal words "the current problem" as the question. A Socratic tutor working from those is not
/// a tutor; it is a well-mannered stranger.
///
/// Note the shape. A summary, a stem, and a passage — **every one of which the learner can already
/// see**. There is no options field and no answer field, so an adapter has nothing to over-supply
/// even if a future one wanted to.
public sealed record TutorItemContext(string Stem, string? Passage);

public interface ITutorContext
{
    /// Null when the item is unknown, or when the session is not bound to one — the caller carries
    /// on without it rather than inventing one.
    Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default);

    /// The concept's summary in words. Null when the Graph has never heard of it.
    Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default);
}
