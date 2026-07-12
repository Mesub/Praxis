namespace Praxis.Modules.Assessment.Domain;

/// **One wrong belief, in one learner, and the receipts for killing it** (M7.5).
///
/// This row is a *projection of the event log*. Nothing here has a public setter, there is no
/// `MarkAsFixed`, and there is no admin route — because **the moment somebody's opinion can close a
/// misconception, the thing that eventually closes it will be fatigue**: a long support queue, a
/// release deadline, a learner who complained.
///
/// The only way this changes is that something happened and was written down.
public sealed class LearnerMisconception
{
    public Guid Id { get; private set; }

    public Guid LearnerId { get; private set; }

    public Guid ConceptId { get; private set; }

    /// A key from the concept's declared set — **or `null` when this belief is learner-local**.
    ///
    /// A new belief is real for the person who holds it and PRAXIS hunts it for them *immediately*.
    /// **We can target a belief we have not named.** What it does not do is become a fact about the
    /// world until 25 other people turn out to hold it and a human curator has agreed (M7.5).
    public string? Key { get; private set; }

    /// **What the learner actually wrote that revealed this belief.** Their words, verbatim.
    ///
    /// Note what this is *not*: it is not the statement of the wrong rule. **A misconception's
    /// wording is a fact about the subject, not about this learner** — *"a price level is a physical
    /// barrier, not a finite pool of liquidity"* is true of everybody who holds it, so it lives once,
    /// in the concept's declared library, keyed by `Key`. Copying it onto every learner who holds it
    /// would be storing the same sentence ten thousand times and then having to keep them in step.
    ///
    /// What lives *here* is the evidence: the thing this person said, which a human curator can read
    /// when they are deciding whether the diagnosis was right.
    public string RevealedBy { get; private set; } = string.Empty;

    public MisconceptionStatus Status { get; private set; } = MisconceptionStatus.Suspected;

    /// One hit is a suspicion. **Two independent hits is a diagnosis** — and only then does PRAXIS
    /// start spending the learner's evenings on it.
    public int IndependentHits { get; private set; }

    /// Clean answers on the discriminating dimension since the last hit. **A suspicion PRAXIS never
    /// lets go of is a suspicion that becomes a fact by attrition.**
    public int CleanItemsSince { get; private set; }

    public int FailedRemediations { get; private set; }

    // ── The receipts (M7.5) ─────────────────────────────────────────────────

    public int TargetedItemsPassed { get; private set; }

    public int DistinctSessions { get; private set; }

    /// **The session the last receipt was earned in**, so that *"one good day is not a repair"* is a
    /// fact this row can check for itself.
    ///
    /// The alternative was to look it up in the `responses` table — and that would make this
    /// projection depend on **another projection having already run**, which is not a projection at
    /// all, it is a race with a database table.
    public Guid? LastReceiptSessionId { get; private set; }

    public bool CritiquedItInSomeoneElsesWork { get; private set; }

    public bool NamedAndRepairedIt { get; private set; }

    public bool SurvivedADifferentModality { get; private set; }

    /// When the repair started, and when it last progressed. **Both come from event timestamps, not
    /// the wall clock** — a projection that reads `DateTime.UtcNow` produces a different answer every
    /// time you replay the log, and at that point the Twin is not a projection, it is a rumour with a
    /// database table.
    public DateTime? RepairBeganAt { get; private set; }

    public DateTime? LastProgressAt { get; private set; }

    public DateTime FirstSeenAt { get; private set; }

    public DateTime LastHitAt { get; private set; }

    private LearnerMisconception() { }

    internal static LearnerMisconception Suspect(
        Guid learnerId, Guid conceptId, string? key, string revealedBy, DateTime at)
        => new()
        {
            Id = Guid.NewGuid(),
            LearnerId = learnerId,
            ConceptId = conceptId,
            Key = key,
            RevealedBy = revealedBy,
            Status = MisconceptionStatus.Suspected,
            IndependentHits = 1,
            FirstSeenAt = at,
            LastHitAt = at,
        };

    /// **A repair that does not survive a sleep cycle is a performance, not a learning** — so the
    /// span is measured across event time, from when the repair began to when it last progressed.
    public int DaysSpanned
        => RepairBeganAt is { } began && LastProgressAt is { } last
            ? Math.Max(0, (int)(last - began).TotalDays)
            : 0;

    /// **The receipts, assembled.** `IsEarned` is arithmetic over facts, and there is no other way to
    /// get there.
    public EradicationEvidence Evidence
        => new(
            TargetedItemsPassed,
            DistinctSessions,
            DaysSpanned,
            CritiquedItInSomeoneElsesWork,
            NamedAndRepairedIt,
            SurvivedADifferentModality);

    /// They got it wrong again, in a way that points at this belief.
    ///
    /// **A relapse from any resolved state goes straight back to `Confirmed`** — not back to the
    /// bottom of the ladder. It is the same old belief and PRAXIS already knows its name.
    internal void Hit(DateTime at)
    {
        if (Status is MisconceptionStatus.Remediated or MisconceptionStatus.Eradicated)
        {
            // The repair failed. That is not the learner's fault, and it is recorded as *our* failed
            // remediation, not their failure to learn.
            FailedRemediations++;

            if (FailedRemediations >= MisconceptionLifecycle.FailedRemediationsToPersistent)
            {
                // **This is not the learner's failure. It is ours.** Escalate: a different
                // representation, a different pedagogy, a human being — or an honest report that this
                // concept is out of reach for now, and a plan built around that fact.
                Status = MisconceptionStatus.Persistent;

                ResetReceipts();
                RecordHit(at);

                return;
            }
        }

        IndependentHits++;

        Status = MisconceptionLifecycle.AfterHit(Status, IndependentHits);

        ResetReceipts();
        RecordHit(at);
    }

    /// A clean answer on the discriminating dimension. Enough of them and the suspicion is dropped —
    /// **PRAXIS is not entitled to hold a suspicion open forever until it happens to be right.**
    internal void CleanAnswer()
    {
        if (Status is not MisconceptionStatus.Suspected)
        {
            return;
        }

        CleanItemsSince++;
    }

    internal bool IsDismissed
        => Status is MisconceptionStatus.Suspected
           && CleanItemsSince >= MisconceptionLifecycle.CleanItemsToDismiss;

    /// A receipt earned. **Nothing here closes the misconception** — `EradicationEvidence.IsEarned`
    /// does, by arithmetic, and only when every single one of them is in.
    internal void Earn(
        DateTime at,
        Guid sessionId,
        bool targetedItemPassed = false,
        bool critiquedIt = false,
        bool namedAndRepairedIt = false,
        bool differentModality = false)
    {
        RepairBeganAt ??= at;
        LastProgressAt = at;

        if (targetedItemPassed)
        {
            TargetedItemsPassed++;
        }

        if (sessionId != LastReceiptSessionId)
        {
            // **One good day is not a repair.** Three targeted items in one evening is one evening.
            DistinctSessions++;
            LastReceiptSessionId = sessionId;
        }

        CritiquedItInSomeoneElsesWork |= critiquedIt;
        NamedAndRepairedIt |= namedAndRepairedIt;
        SurvivedADifferentModality |= differentModality;

        if (Status is MisconceptionStatus.Confirmed && (namedAndRepairedIt || critiquedIt))
        {
            // **The learner produced the correct discrimination themselves.** The only transition in
            // the lifecycle they perform rather than receive, and the one that predicts everything
            // after it.
            Status = MisconceptionStatus.Challenged;
        }

        if (Evidence.IsEarned && Status is MisconceptionStatus.Challenged or MisconceptionStatus.Confirmed)
        {
            Status = MisconceptionStatus.Remediated;
        }
    }

    private void ResetReceipts()
    {
        // A relapse invalidates the receipts. They were evidence that the belief was gone; it isn't.
        TargetedItemsPassed = 0;
        DistinctSessions = 0;
        LastReceiptSessionId = null;
        CritiquedItInSomeoneElsesWork = false;
        NamedAndRepairedIt = false;
        SurvivedADifferentModality = false;
        RepairBeganAt = null;
        LastProgressAt = null;
    }

    private void RecordHit(DateTime at)
    {
        LastHitAt = at;
        CleanItemsSince = 0;
    }
}
