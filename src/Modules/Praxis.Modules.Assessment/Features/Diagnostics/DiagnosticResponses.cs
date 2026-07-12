using Praxis.Modules.Assessment.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.Diagnostics;

/// **The next question — or an honest reason there isn't one.**
///
/// There is no `Answer` on this record, no `Solution`, no `Hint`, and no `Explanation`. The handler
/// that built it never had any of those and could not have obtained them: the item it read from the
/// bank has no answer column, and there is no endpoint anywhere in the system that returns one.
public sealed record NextItemResponse(
    string? ItemId,
    string? Stem,
    string? EvidenceType,

    /// What PRAXIS currently believes, **and how sure it is** — always together. θ on its own is a
    /// number, and a number on its own always looks like knowledge.
    double Theta,
    double StandardError,
    bool ConfidentEnoughToStop,

    /// **PRAXIS has stopped asking.** Not because the learner failed — because carrying on would be
    /// a machine correctly maximising the wrong thing at a human being.
    bool Stopped,

    string? Says)
{
    public static NextItemResponse Ask(BankedItem item, Ability ability)
        => new(
            ItemId: item.Id.ToString(),
            Stem: item.Stem,
            EvidenceType: item.EvidenceType.ToString(),
            Theta: ability.Theta,
            StandardError: ability.StandardError,
            ConfidentEnoughToStop: ability.IsConfident,
            Stopped: false,
            Says: null);

    /// **"We do not keep hammering someone who is drowning."** Also: *tiredness is not ignorance*.
    ///
    /// This is a success, not an error. Nothing went wrong, and the learner is not being told they
    /// failed — PRAXIS is telling them it has stopped asking, and why.
    ///
    /// **The ability travels with it, honestly.** The obvious thing is to zero the fields out because
    /// there is no item — and a `StandardError` of zero is a claim of *perfect certainty*, which is
    /// the precise lie that field exists to prevent. PRAXIS stopping because a learner is tired says
    /// nothing at all about how well it knows them, so it reports what it actually believes.
    public static NextItemResponse Stop(string says, Ability ability)
    {
        ArgumentNullException.ThrowIfNull(ability);

        return new(
            ItemId: null,
            Stem: null,
            EvidenceType: null,
            Theta: ability.Theta,
            StandardError: ability.StandardError,
            ConfidentEnoughToStop: ability.IsConfident,
            Stopped: true,
            Says: says);
    }
}
