using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Persistence;

public sealed class TutorDbContext : DbContext
{
    public const string Schema = "tutor";

    public DbSet<AnswerBudget> Budgets => Set<AnswerBudget>();

    public DbSet<Session> Sessions => Set<Session>();

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

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("sessions");

            session.HasKey(s => s.Id);

            session.HasIndex(s => new { s.LearnerId, s.ConceptId });

            session.Property(s => s.State).IsRequired();
            session.Property(s => s.Outcome);
            session.Property(s => s.Produced).IsRequired();
            session.Property(s => s.StuckNotes).IsRequired();
            session.Property(s => s.HintsTaken).IsRequired();
            session.Property(s => s.AwaitingLearner).IsRequired();

            // Derived, never stored. A stored "did they make something" is a second version of the
            // truth, and it is the version somebody would eventually set to `true` to unblock a
            // release.
            session.Ignore(s => s.MadeSomething);
        });

        base.OnModelCreating(modelBuilder);
    }
}
