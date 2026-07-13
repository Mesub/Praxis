using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Domain;

namespace Praxis.Modules.Graph.Persistence;

public sealed class GraphDbContext : DbContext
{
    public const string Schema = "graph";

    public GraphDbContext(DbContextOptions<GraphDbContext> options) : base(options) { }

    public DbSet<Concept> Concepts => Set<Concept>();
    public DbSet<ConceptEdge> Edges => Set<ConceptEdge>();
    public DbSet<GraphVersion> Versions => Set<GraphVersion>();
    public DbSet<PoMContract> Contracts => Set<PoMContract>();
    public DbSet<ConceptMisconception> Misconceptions => Set<ConceptMisconception>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Concept>(e =>
        {
            e.ToTable("concepts", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(60).IsRequired();
            e.Property(x => x.DifficultyPrior).IsRequired();
            e.Property(x => x.EstimatedMinutes).IsRequired();

            // The slug is how a domain pack, an item bank and a piece of Evidence all refer to
            // the same idea. Two concepts sharing one would silently merge three things.
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.Domain);

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ConceptEdge>(e =>
        {
            e.ToTable("concept_edges", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Kind).HasConversion<int>().IsRequired();
            e.Property(x => x.Provenance).HasConversion<int>().IsRequired();
            e.Property(x => x.Strength).IsRequired();

            e.HasOne<Concept>().WithMany()
             .HasForeignKey(x => x.FromConceptId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Concept>().WithMany()
             .HasForeignKey(x => x.ToConceptId)
             .OnDelete(DeleteBehavior.Restrict);

            // One claim per pair per kind. Two identical REQUIRES edges would double-count in
            // the Compiler's path cost.
            e.HasIndex(x => new { x.FromConceptId, x.ToConceptId, x.Kind }).IsUnique();

            // The hot paths: "what does this need" and "what needs this".
            e.HasIndex(x => new { x.ToConceptId, x.Kind });
            e.HasIndex(x => new { x.FromConceptId, x.Kind });

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PoMContract>(e =>
        {
            e.ToTable("pom_contracts", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.HoldThreshold).IsRequired();

            // One contract per concept. Two would mean two different answers to "what would
            // prove you understand this", and the Twin would have to pick one.
            e.HasIndex(x => x.ConceptId).IsUnique();

            e.HasOne<Concept>().WithMany()
             .HasForeignKey(x => x.ConceptId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Accepted).WithOne()
             .HasForeignKey(a => a.PoMContractId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Accepted).AutoInclude();

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<AcceptedEvidence>(e =>
        {
            e.ToTable("accepted_evidence", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Type).HasConversion<int>().IsRequired();
            e.Property(x => x.MinScore).IsRequired();
            e.Property(x => x.MaxContribution).IsRequired();
            e.Property(x => x.RequiredCount).IsRequired();

            // Each evidence type gets one clause per contract.
            e.HasIndex(x => new { x.PoMContractId, x.Type }).IsUnique();

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ConceptMisconception>(e =>
        {
            e.ToTable("concept_misconceptions", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.Statement).HasMaxLength(2000).IsRequired();

            e.HasOne<Concept>().WithMany()
             .HasForeignKey(x => x.ConceptId)
             .OnDelete(DeleteBehavior.Cascade);

            // The key names one wrong idea across the whole graph — the same uniqueness the pack
            // validator enforces at author time, now enforced by the database at load time.
            e.HasIndex(x => x.Key).IsUnique();
            e.HasIndex(x => x.ConceptId);

            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<GraphVersion>(e =>
        {
            e.ToTable("graph_versions", Schema);
            e.HasKey(x => x.Id);

            e.Property(x => x.Domain).HasMaxLength(60).IsRequired();
            e.Property(x => x.Note).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Number).IsRequired();
            e.Property(x => x.PublishedAt).IsRequired();

            e.HasIndex(x => new { x.Domain, x.Number }).IsUnique();

            e.Property(x => x.Version).IsRowVersion();
        });

        base.OnModelCreating(modelBuilder);
    }
}
