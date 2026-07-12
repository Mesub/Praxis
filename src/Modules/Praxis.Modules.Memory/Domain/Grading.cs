namespace Praxis.Modules.Memory.Domain;

/// **Turning what a learner actually did into a grade the memory model understands.**
///
/// Every flashcard app in the world asks the learner to do this themselves: *"was that easy, or
/// hard?"* PRAXIS does not, and will not — **asking somebody to grade their own recall is asking
/// them to self-report their confidence at the exact moment they have just been shown the answer**,
/// which is when human beings are least able to do it (C-6). The whole product exists because that
/// self-report is unreliable.
///
/// So the grade is *derived*, from what happened.
public static class Grading
{
    /// What PRAXIS can honestly infer today.
    ///
    /// ## Why there is no `Hard` and no `Easy` here yet
    ///
    /// Those two grades are the difference between *"right, but it was a struggle"* and *"right,
    /// instantly"* — and the only honest way to tell them apart is **latency against a baseline**: how
    /// long *this* learner took, against how long *this item* usually takes.
    ///
    /// PRAXIS records the latency (`AnswerGiven.Seconds`). It does **not** yet have the per-item
    /// baseline to compare it against — item calibration reaches `Calibrated` at 200 responses (M7.2),
    /// and until then a "slow" answer might just be a slow item.
    ///
    /// **So PRAXIS does not guess.** A guessed grade is not a small inaccuracy: `Hard` and `Easy` sit
    /// on either side of `Good` and pull stability in opposite directions, so guessing wrong does not
    /// add noise — it adds *bias*, in a model of somebody's memory, which then schedules their
    /// evenings.
    ///
    /// Two grades that are true beat four grades that are plausible. When the baselines exist, this
    /// is where the other two arrive.
    public static Grade Of(bool correct)
        => correct ? Grade.Good : Grade.Again;
}
