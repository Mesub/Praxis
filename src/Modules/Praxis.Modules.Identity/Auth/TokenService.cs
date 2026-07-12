using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Praxis.Infrastructure.Web;
using Praxis.Modules.Identity.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Identity.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "praxis";
    public string Audience { get; set; } = "praxis";

    /// No default. A signing key with a fallback is a signing key someone ships to production.
    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeMinutes { get; set; } = 60 * 12;
}

public interface ITokenService
{
    AccessToken IssueFor(Learner learner);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public sealed class TokenService : ITokenService
{
    /// Anything shorter than 32 bytes is not a 256-bit key, and HS256 will happily sign with it
    /// anyway. Refuse at startup rather than issue weak tokens quietly.
    private const int MinimumKeyBytes = 32;

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    public TokenService(JwtOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            throw new InvalidOperationException(
                "No JWT signing key is configured (Jwt:SigningKey). Set one in "
                + "appsettings.Development.json or the environment. There is no default, on "
                + "purpose — a fallback key is a key that reaches production.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(options.SigningKey);

        if (keyBytes.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key is {keyBytes.Length} bytes. HS256 needs at least "
                + $"{MinimumKeyBytes} and will sign with a shorter one without complaining.");
        }

        _options = options;
        _clock = clock;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public AccessToken IssueFor(Learner learner)
    {
        ArgumentNullException.ThrowIfNull(learner);

        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(HttpUserContext.LearnerIdClaim, learner.Id.ToString()),
            new(JwtRegisteredClaimNames.Sub, learner.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, learner.Email),
            new(JwtRegisteredClaimNames.Name, learner.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
