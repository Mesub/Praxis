using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Projections;

namespace Praxis.Modules.Kernel.Persistence;

/// The event log. One table, append-only, and the only durable record of learning in PRAXIS.
///
/// Append-only is enforced twice, on purpose:
///   1. here, so a mistake fails loudly in a test rather than silently in production;
///   2. in the database, by a trigger (see the migration), so that it holds even against a
///      developer with psql open and good intentions.
///
/// Defence 1 is a courtesy. Defence 2 is the guarantee.
public sealed class KernelDbContext : DbContext
{
    public const string Schema = "kernel";

    public KernelDbContext(DbContextOptions<KernelDbContext> options) : base(options) { }

    public DbSet<LearningEvent> Events => Set<LearningEvent>();

    /// How far each projection has read. The only mutable rows the kernel owns — and they hold
    /// no knowledge, only bookmarks.
    public DbSet<ProjectionCheckpoint> Checkpoints => Set<ProjectionCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<LearningEvent>(e =>
        {
            e.ToTable("learning_events", Schema);

            // The sequence is the log's spine: a gapless, database-assigned append order.
            // We never set it; two concurrent appends must not be able to agree on a value.
            e.HasKey(x => x.Sequence);
            e.Property(x => x.Sequence)
             .UseIdentityAlwaysColumn();

            e.Property(x => x.Id).IsRequired();
            e.HasAlternateKey(x => x.Id);

            e.Property(x => x.LearnerId).IsRequired();

            e.Property(x => x.Type)
             .HasConversion<int>()
             .IsRequired();

            e.Property(x => x.ConceptId);

            e.Property(x => x.Payload)
             .HasColumnType("jsonb")
             .IsRequired();

            e.Property(x => x.OccurredAt).IsRequired();
            e.Property(x => x.RecordedAt).IsRequired();

            // Reading one learner's stream, in order, is the hot path: it is what rebuilding
            // a Twin does.
            e.HasIndex(x => new { x.LearnerId, x.Sequence });

            // Decay and rep scheduling ask "what happened to this concept, and when".
            e.HasIndex(x => new { x.LearnerId, x.ConceptId, x.OccurredAt });
        });

        modelBuilder.Entity<ProjectionCheckpoint>(c =>
        {
            c.ToTable("projection_checkpoints", Schema);

            c.HasKey(x => x.ProjectionName);
            c.Property(x => x.ProjectionName).HasMaxLength(200);

            c.Property(x => x.LastSequence).IsRequired();
            c.Property(x => x.UpdatedAt).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectMutations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectMutations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// The log grows. It does not change.
    private void RejectMutations()
    {
        foreach (EntityEntry<LearningEvent> entry in ChangeTracker.Entries<LearningEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"Attempted to {entry.State.ToString().ToUpperInvariant()} a LearningEvent "
                    + "(sequence "
                    + $"{entry.Entity.Sequence}). The event log is append-only: the Twin, the "
                    + "Ledger and every analytic in PRAXIS are projections of it, and all of them "
                    + "become lies the moment history can be edited.");
            }
        }
    }
}
