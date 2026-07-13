using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;

namespace Praxis.Modules.Analytics.Domain;

/// **The denominator, straight from the log — and it counts the minutes you actually spent, not the
/// ones that "worked".**
///
/// A session you *abandoned* still cost you the minutes you were in it, so those count too: the honest
/// question is how much of your life PRAXIS took, and a metric that quietly forgave the time you spent
/// getting nowhere would be flattering exactly the weeks it should be reporting. Planned minutes are
/// **not** counted — you are measured on what you did, never on what you meant to do.
///
/// It reads the immutable log, so it is a pure fold of events and needs no clock: the same stream
/// always yields the same minutes, which is what lets the whole metric be a function rather than a
/// query.
public static class MinutesInvested
{
    public static double From(IReadOnlyList<LearningEvent> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var minutes = 0.0;

        foreach (var e in stream)
        {
            switch (e.Type)
            {
                case LearningEventType.SessionCompleted:
                    minutes += EventCatalogue.Deserialize<SessionCompleted>(e).ActualMinutes;
                    break;

                case LearningEventType.SessionAbandoned:
                    // Abandoned time is still time. It is not forgiven, because forgiving it would
                    // flatter the weeks that most need the truth.
                    minutes += EventCatalogue.Deserialize<SessionAbandoned>(e).MinutesIn;
                    break;
            }
        }

        return minutes;
    }
}
