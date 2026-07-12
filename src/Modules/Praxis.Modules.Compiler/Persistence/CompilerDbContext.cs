using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Compiler.Domain;

namespace Praxis.Modules.Compiler.Persistence;

public sealed class CompilerDbContext : DbContext
{
    public const string Schema = "compiler";

    public CompilerDbContext(DbContextOptions<CompilerDbContext> options) : base(options) { }

    public DbSet<Objective> Objectives => Set<Objective>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Objective>(e =>
        {
            e.ToTable("objectives", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.MinutesPerDay).IsRequired();
            e.Property(x => x.CompiledAtSequence).IsRequired();

            // **One active objective at a time.**
            //
            // Not a technical constraint — a product one. A learner pursuing three objectives is
            // pursuing none of them, and the Scheduler would have to arbitrate between them every
            // morning. PRAXIS asks people to choose (roadmap §1: "One Objective. Not a track. One.")
            e.HasIndex(x => x.LearnerId)
             .IsUnique()
             .HasFilter("\"IsActive\" AND NOT \"IsDeleted\"");

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        base.OnModelCreating(modelBuilder);
    }
}
