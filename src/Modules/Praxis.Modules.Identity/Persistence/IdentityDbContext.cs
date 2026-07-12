using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Identity.Domain;

namespace Praxis.Modules.Identity.Persistence;

public sealed class IdentityDbContext : DbContext
{
    public const string Schema = "identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<Learner> Learners => Set<Learner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Learner>(e =>
        {
            e.ToTable("learners", Schema);

            e.HasKey(x => x.Id);

            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.NormalisedEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();

            // One account per address. Enforced in the database, not just in a handler that
            // could lose a race with itself.
            e.HasIndex(x => x.NormalisedEmail).IsUnique();

            e.Property(x => x.Version).IsRowVersion();

            e.HasQueryFilter(x => !x.IsDeleted);
        });

        base.OnModelCreating(modelBuilder);
    }
}
