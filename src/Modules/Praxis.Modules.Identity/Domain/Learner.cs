using Praxis.Shared;

namespace Praxis.Modules.Identity.Domain;

/// A human being.
///
/// Deliberately thin. PRAXIS knows a great deal about this person — it holds the most intimate
/// model of their mind anyone has ever built — but **none of that lives here.** It lives in the
/// Twin, which is a projection of their evidence, and it is theirs (CANON §5.9).
///
/// This row is the key their history hangs from, and nothing more.
public sealed class Learner : BaseEntity
{
    public string Email { get; private set; } = null!;

    /// Normalised for lookup. The learner's own casing is preserved in <see cref="Email"/>,
    /// because it is their name, not ours.
    public string NormalisedEmail { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public DateTime? LastLoginAt { get; private set; }

    private Learner() { }   // EF

    public static Learner Register(string email, string displayName, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var trimmed = email.Trim();

        return new Learner
        {
            Email = trimmed,
            NormalisedEmail = Normalise(trimmed),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
        };
    }

    public void RecordLogin(DateTime at) => LastLoginAt = at;

    public void RehashPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public static string Normalise(string email) => email.Trim().ToUpperInvariant();
}
