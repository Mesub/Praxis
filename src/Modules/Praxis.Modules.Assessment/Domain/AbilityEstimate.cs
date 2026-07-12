namespace Praxis.Modules.Assessment.Domain;

/// **What PRAXIS currently believes about one learner on one concept, and how sure it is.**
///
/// The second half of that sentence is the important one. `Theta` on its own is a number, and a
/// number on its own always looks like knowledge. `StandardError` is the part that says *"and we
/// might be quite wrong about this"* — which is why the two are stored together and **`Theta` is
/// never exposed without it** anywhere in the product.
///
/// ## It is a projection, and that is the whole point
///
/// This row is recomputed by replaying `AnswerGiven` events through the IRT estimator (M7.2). It can
/// be thrown away and rebuilt from zero, and `AbilityProjectionTests` proves it every build.
///
/// **There is no setter.** Nobody can adjust a learner's ability — not support, not an admin, not a
/// migration after a bad week. The only way to change what PRAXIS believes about somebody is to
/// **append an event saying they did something**, and let the estimator draw its own conclusion. If
/// that sounds inconvenient, it is: it is inconvenient in exactly the way that stops a belief about a
/// person's mind being edited by someone in a hurry.
public sealed class AbilityEstimate
{
    public Guid Id { get; private set; }

    public Guid LearnerId { get; private set; }

    public Guid ConceptId { get; private set; }

    /// Logit scale, centred on 0. Meaningless without `StandardError` beside it.
    public double Theta { get; private set; }

    /// **How wrong we might be.** Starts at 1.5 — which is to say *"we have no idea"* — and shrinks
    /// only as evidence arrives.
    public double StandardError { get; private set; } = Ability.Unknown.StandardError;

    public int Responses { get; private set; }

    private AbilityEstimate() { }

    internal static AbilityEstimate Unknown(Guid learnerId, Guid conceptId)
        => new()
        {
            Id = Guid.NewGuid(),
            LearnerId = learnerId,
            ConceptId = conceptId,
            Theta = Ability.Unknown.Theta,
            StandardError = Ability.Unknown.StandardError,
            Responses = 0,
        };

    public Ability Current => new(Theta, StandardError);

    /// **PRAXIS is confident enough to act on this.** Below the threshold it says *"too early to
    /// say"* out loud rather than quietly turning noise into a finding.
    public bool IsConfident => Current.IsConfident;

    /// Only the projection calls this, and only with a posterior the estimator computed from the
    /// whole history of responses.
    internal void Revise(Ability posterior, int responses)
    {
        Theta = posterior.Theta;
        StandardError = posterior.StandardError;
        Responses = responses;
    }
}
