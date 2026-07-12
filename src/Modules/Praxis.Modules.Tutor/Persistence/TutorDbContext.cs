using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Persistence;

public sealed class TutorDbContext : DbContext
{
    public const string Schema = "tutor";

    public DbSet<AnswerBudget> Budgets => Set<AnswerBudget>();

    public TutorDbContext(DbContextOptions<TutorDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AnswerBudget>(budget =>
        {
            budget.ToTable("answer_budgets");

            budget.HasKey(b => b.Id);

            // One budget per learner per concept. The database enforces it, because two budgets for
            // one concept is two chances to spend, and the second one would be free.
            budget.HasIndex(b => new { b.LearnerId, b.ConceptId }).IsUnique();

            budget.Property(b => b.Allowance).IsRequired();
            budget.Property(b => b.Spent).IsRequired();
            budget.Property(b => b.HintsTaken).IsRequired();
            budget.Property(b => b.DeepestRung);

            // Computed, never stored. A stored `Remaining` is a second version of the truth, and it
            // is the version somebody would eventually write to.
            budget.Ignore(b => b.Remaining);
            budget.Ignore(b => b.IsExhausted);
        });

        base.OnModelCreating(modelBuilder);
    }
}
