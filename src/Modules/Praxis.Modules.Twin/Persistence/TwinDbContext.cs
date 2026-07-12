using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Domain;

namespace Praxis.Modules.Twin.Persistence;

/// The Twin's tables.
///
/// **Every row in here is derived.** Nothing is a source of truth; all of it is recomputed by
/// replaying the event log (M1.6). If a value appears here that no event justifies, a rebuild
/// deletes it — and that is the point.
public sealed class TwinDbContext : DbContext
{
    public const string Schema = "twin";

    public TwinDbContext(DbContextOptions<TwinDbContext> options) : base(options) { }

    public DbSet<ConceptState> States => Set<ConceptState>();
    public DbSet<Misconception> Misconceptions => Set<Misconception>();

    /// The Twin's working memory of what the learner produced. Derived, and thrown away by a
    /// rebuild — the Ledger (E16) is the record of truth, not this.
    public DbSet<EvidenceItem> Evidence => Set<EvidenceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ConceptState>(e =>
        {
            e.ToTable("concept_states", Schema);
            e.HasKey(x => x.Id);

            // One belief per learner per concept. Two would mean the Twin holds two opinions about
            // the same person and has to pick one.
            e.HasIndex(x => new { x.LearnerId, x.ConceptId }).IsUnique();

            e.Property(x => x.PKnows).IsRequired();
            e.Property(x => x.MasteryCredit).IsRequired();
            e.Property(x => x.IsHeld).IsRequired();

            e.Property(x => x.Stability).IsRequired();
            e.Property(x => x.Difficulty).IsRequired();

            e.Property(x => x.MeanStatedConfidence).IsRequired();
            e.Property(x => x.MeanCorrectness).IsRequired();
            e.Property(x => x.BrierScore).IsRequired();
            e.Property(x => x.CalibrationSamples).IsRequired();

            e.Property(x => x.Assurance).IsRequired();
            e.Property(x => x.EvidenceCount).IsRequired();

            // ConfidenceGap is computed, not stored — a stored copy is a second version of the
            // truth, and it will drift.
            e.Ignore(x => x.ConfidenceGap);

            // The rep queue's hot path: "what is rotting, and when".
            e.HasIndex(x => new { x.LearnerId, x.NextDueAt });

            // The Compiler's hot path: "what does this learner already hold".
            e.HasIndex(x => new { x.LearnerId, x.IsHeld });

            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<EvidenceItem>(e =>
        {
            e.ToTable("evidence_items", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Type).HasConversion<int>().IsRequired();
            e.Property(x => x.Score).IsRequired();
            e.Property(x => x.Assurance).IsRequired();
            e.Property(x => x.OccurredAt).IsRequired();

            // Replaying an event that was already applied must not count it twice, or a rebuild
            // would inflate every learner in the system.
            e.HasIndex(x => x.EvidenceId).IsUnique();

            e.HasIndex(x => new { x.LearnerId, x.ConceptId });

            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<Misconception>(e =>
        {
            e.ToTable("misconceptions", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.Statement).HasMaxLength(1000).IsRequired();

            // The same wrong idea, in the same learner, on the same concept, is one misconception —
            // observed many times, not many misconceptions.
            e.HasIndex(x => new { x.LearnerId, x.ConceptId, x.Key }).IsUnique();

            e.HasIndex(x => new { x.LearnerId, x.IsActive });

            e.Property(x => x.Version).IsRowVersion();
        });

        base.OnModelCreating(modelBuilder);
    }
}
