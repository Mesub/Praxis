namespace Praxis.Modules.Tutor;

/// How the tutor asks a concept's **declared misconception keys** — the closed set of wrong beliefs
/// it is allowed to name — **without knowing the Graph exists**.
///
/// The Graph authors and owns the library; the tutor only needs the keys, so it can hand them to the
/// AI plane as `KnownMisconceptions` and have a learner's wrong answer matched against a named,
/// mineable belief rather than a fresh invention (I-6). Neither module references the other — the
/// keys travel as plain strings, and this port is implemented at the composition root.
///
/// A concept that declares no misconceptions returns an empty list, never null: the tutor asks, and
/// "none yet" is a real answer.
public interface IMisconceptionLibrary
{
    Task<IReadOnlyList<string>> KeysForConceptAsync(Guid conceptId, CancellationToken ct = default);
}
