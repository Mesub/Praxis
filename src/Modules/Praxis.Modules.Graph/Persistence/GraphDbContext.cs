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
