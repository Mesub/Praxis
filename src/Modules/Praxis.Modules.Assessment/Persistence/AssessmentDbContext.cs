using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Domain;

namespace Praxis.Modules.Assessment.Persistence;

/// **The Assessment schema — and the most important thing in it is a column that does not exist.**
///
/// `items` has a stem, a difficulty, a fingerprint, and the key of the wrong belief its distractors
/// are built to catch. It does **not** have an answer, a solution, an expected value, or a key. Not
/// nullable, not encrypted, not access-controlled: **absent**.
///
/// The answer went to the oracle's store on the far side of the AI-plane wire the moment the item was
/// written (M5.5), and the only thing that has ever come back is one of three words. So the process
/// that serves the tutor has **no answer in it to leak** — which is why a jailbreak against the tutor
/// retrieves nothing, and why that is a fact about the deployment rather than a hope about a prompt.
///
/// `SchemaTests` reads `information_schema` — the real database, not these C# types — and fails the
/// build if a column ever appears here whose name looks like an answer. That test is deliberately
/// paranoid, because the failure it prevents is silent: a migration adds `expected_output` for some
/// perfectly good reason, and PRAXIS's central claim quietly becomes false while every other test
/// stays green.
public sealed class AssessmentDbContext : DbContext
{
    public const string Schema = "assessment";

    /// The item bank. **No answer column.** See above; it is the whole point.
    public DbSet<BankedItem> Items => Set<BankedItem>();

    /// One wrong belief, in one learner, with the receipts for killing it.
    public DbSet<LearnerMisconception> Misconceptions => Set<LearnerMisconception>();

    /// What PRAXIS believes about a learner, and **how sure it is** — always together.
    public DbSet<AbilityEstimate> Abilities => Set<AbilityEstimate>();

    /// Assessment's own view of what the learner has done. Derived from `AnswerGiven`, and rebuilt
    /// from zero on demand — **no module reads another module's tables**, so Assessment keeps the
    /// slice of history it needs rather than reaching into the kernel's.
    public DbSet<RecordedResponse> Responses => Set<RecordedResponse>();

    public AssessmentDbContext(DbContextOptions<AssessmentDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<BankedItem>(item =>
        {
            item.ToTable("items");

            item.HasKey(i => i.Id);

            item.HasIndex(i => new { i.ConceptId, i.EvidenceType });

            // Novelty is a fact, not a hope (M5.5). One fingerprint, one item.
            item.HasIndex(i => i.Fingerprint).IsUnique();

            // The selector's hot path: "which items diagnose *this* wrong belief?" This index is I-6
            // made queryable — the difference between hunting a misconception and grinding a bank.
            item.HasIndex(i => new { i.ConceptId, i.TargetsMisconception });

            item.Property(i => i.Stem).IsRequired().HasMaxLength(8_000);
            item.Property(i => i.Fingerprint).IsRequired().HasMaxLength(128);
            item.Property(i => i.EvidenceType).IsRequired();
            item.Property(i => i.Difficulty).IsRequired();
            item.Property(i => i.Discrimination).IsRequired();
            item.Property(i => i.Guessing).IsRequired();
            item.Property(i => i.TargetsMisconception).HasMaxLength(64);
            item.Property(i => i.TimesServed).IsRequired();
            item.Property(i => i.TimesCorrect).IsRequired();
        });

        modelBuilder.Entity<LearnerMisconception>(mc =>
        {
            mc.ToTable("learner_misconceptions");

            mc.HasKey(m => m.Id);

            mc.HasIndex(m => new { m.LearnerId, m.ConceptId });

            mc.Property(m => m.Rule).IsRequired().HasMaxLength(1_000);
            mc.Property(m => m.Key).HasMaxLength(64);
            mc.Property(m => m.Status).IsRequired();
            mc.Property(m => m.IndependentHits).IsRequired();
            mc.Property(m => m.CleanItemsSince).IsRequired();
            mc.Property(m => m.FailedRemediations).IsRequired();

            mc.Property(m => m.TargetedItemsPassed).IsRequired();
            mc.Property(m => m.DistinctSessions).IsRequired();
            mc.Property(m => m.CritiquedItInSomeoneElsesWork).IsRequired();
            mc.Property(m => m.NamedAndRepairedIt).IsRequired();
            mc.Property(m => m.SurvivedADifferentModality).IsRequired();

            // Derived from event timestamps, never stored. A stored `DaysSpanned` is a second version
            // of the truth, and it is the version somebody would eventually write to in order to
            // close a misconception on a Friday.
            mc.Ignore(m => m.DaysSpanned);
            mc.Ignore(m => m.Evidence);
        });

        modelBuilder.Entity<AbilityEstimate>(ability =>
        {
            ability.ToTable("abilities");

            ability.HasKey(a => a.Id);

            // One belief per learner per concept. Two rows would be two answers to *"what does PRAXIS
            // think of you"*, and somebody would eventually read the flattering one.
            ability.HasIndex(a => new { a.LearnerId, a.ConceptId }).IsUnique();

            ability.Property(a => a.Theta).IsRequired();
            ability.Property(a => a.StandardError).IsRequired();
            ability.Property(a => a.Responses).IsRequired();

            ability.Ignore(a => a.Current);
            ability.Ignore(a => a.IsConfident);
        });

        modelBuilder.Entity<RecordedResponse>(response =>
        {
            response.ToTable("responses");

            response.HasKey(r => r.Id);

            // The estimator's hot path: every response this learner has given on this concept.
            response.HasIndex(r => new { r.LearnerId, r.ConceptId });

            // One row per event. If the runner ever redelivers, the database says no rather than
            // letting a learner be scored twice for one answer.
            response.HasIndex(r => r.EventId).IsUnique();

            response.Property(r => r.EventId).IsRequired();
            response.Property(r => r.Correct).IsRequired();
            response.Property(r => r.StatedConfidence).IsRequired();
            response.Property(r => r.Seconds).IsRequired();
            response.Property(r => r.OccurredAt).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
