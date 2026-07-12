using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Domain;
using Praxis.Modules.Identity.Persistence;

namespace Praxis.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString,
        JwtOptions jwt)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwt);

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", IdentityDbContext.Schema)));

        services.AddSingleton(jwt);
        services.AddSingleton<ITokenService, TokenService>();

        // PBKDF2 with the current ASP.NET defaults. Not homemade, and the work factor rises with
        // the framework — old hashes upgrade on next login (see LoginLearner).
        services.AddSingleton<IPasswordHasher<Learner>, PasswordHasher<Learner>>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

                    // No grace period on expiry. The default five minutes is a courtesy to clocks,
                    // and we control both ends.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddAuthorization();

        return services;
    }
}
