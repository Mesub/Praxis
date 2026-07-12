namespace Praxis.Shared;

/// Who is making this request. Populated per-request from the JWT (M1.5) and injected into
/// handlers and interceptors — never read from HttpContext deep inside a slice.
public interface IUserContext
{
    /// The learner making the request.
    Guid? LearnerId { get; }

    bool IsAuthenticated { get; }

    /// Throws when called on an anonymous request. Use inside slices that are already
    /// behind [Authorize], where anonymity means the pipeline is broken, not that the
    /// caller needs a nicer error.
    Guid RequireLearnerId();
}

/// A clock, so that decay and scheduling — the two places in PRAXIS where time IS the
/// domain — can be tested without waiting 90 days for a concept to rot.
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
