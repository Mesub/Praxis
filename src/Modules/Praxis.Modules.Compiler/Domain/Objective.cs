using Praxis.Shared;

namespace Praxis.Modules.Compiler.Domain;

/// **What the learner is trying to become.** The build target.
///
/// Not a course they enrolled in. Not a track. One thing they want to be able to *do*, and the time
/// they actually have — which is the input the Decay Wall interrogates (M4.3), and the reason PRAXIS
/// is sometimes obliged to tell them no.
public sealed class Objective : BaseEntity
{
    public Guid LearnerId { get; private set; }

    /// The concept that *is* the objective. In the seed pack: `beating-the-baseline`.
    public Guid ConceptId { get; private set; }

    /// The honest number, not the aspirational one.
    ///
    /// A learner who says 90 and does 20 is not committed — they are being lied to by their own
    /// optimism, and PRAXIS compiling against 90 makes it complicit. The Contract (M11.4) is where
    /// they are asked to be honest; this is where the honesty is used.
    public int MinutesPerDay { get; private set; }

    public DateTime? Deadline { get; private set; }

    /// When the plan was last compiled. Null means never — they have an objective and no plan, which
    /// is the state a new learner is in for exactly as long as it takes to compile one.
    public DateTime? LastCompiledAt { get; private set; }

    /// The event sequence the plan was compiled against.
    ///
    /// **This is what makes staleness detectable.** A plan is a photograph of a learner at a moment;
    /// the log has moved on since, and this says by how much.
    public long CompiledAtSequence { get; private set; }

    public bool IsActive { get; private set; } = true;

    private Objective() { }   // EF

    public static Objective Set(Guid learnerId, Guid conceptId, int minutesPerDay, DateTime? deadline = null)
    {
        if (minutesPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutesPerDay), minutesPerDay,
                "An objective with no minutes behind it is a wish. PRAXIS compiles plans, not wishes.");
        }

        return new Objective
        {
            LearnerId = learnerId,
            ConceptId = conceptId,
            MinutesPerDay = minutesPerDay,
            Deadline = deadline,
        };
    }

    public void RecordCompile(DateTime at, long sequence)
    {
        LastCompiledAt = at;
        CompiledAtSequence = sequence;
    }

    /// The learner renegotiated. Usually downwards, and usually because PRAXIS told them the truth
    /// about what their minutes could buy — which is the system working, not failing.
    public void Repace(int minutesPerDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minutesPerDay);

        MinutesPerDay = minutesPerDay;

        // The plan compiled against the old pace is now a plan for a different person.
        LastCompiledAt = null;
    }

    public void Abandon() => IsActive = false;
}
