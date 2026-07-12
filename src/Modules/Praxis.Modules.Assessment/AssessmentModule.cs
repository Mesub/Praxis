using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Assessment.Persistence;

namespace Praxis.Modules.Assessment;

public static class AssessmentModule
{
    public static IServiceCollection AddAssessment(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<AssessmentDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", AssessmentDbContext.Schema)));

        // **The projections are NOT registered here**, and that is deliberate rather than an
        // omission. They are registered in `Program.cs` with `AddProjection<T>()`, alongside every
        // other module's — because the composition root is the one place a human can read the whole
        // list of things that derive a belief about a learner, and a projection hidden inside a
        // module's own registration is a projection nobody audits.
        //
        // Registering them in both places is not merely redundant: the `ProjectionRunner` refuses to
        // start with two projections sharing a name, because the name is the checkpoint key and they
        // would overwrite each other's place in history. It caught me doing exactly that.
        return services;
    }
}
