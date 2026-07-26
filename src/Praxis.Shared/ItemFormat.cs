namespace Praxis.Shared;

/// **The shape of an item, and therefore of the evidence it can produce.**
///
/// `Prose` is everything PRAXIS banked before M17: one question, answered in the learner's own
/// words, marked by a model that holds the answer on the far side of the AI-plane wire.
///
/// `MultipleChoice` is the other kind, and it is not a convenience. A fixed-key item is marked by
/// **comparing two integers** — no model, no judgement, no opinion — which makes it the purest
/// Apply evidence PRAXIS can collect, and the only kind whose correctness is a *fact*. The options
/// are on the learner's screen and are not secret. The index of the correct one is in a different
/// database, and this process cannot reach it.
public enum ItemFormat
{
    Prose = 0,
    MultipleChoice = 1,
}
