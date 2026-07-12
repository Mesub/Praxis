using Praxis.Shared;

namespace Praxis.Modules.Reality.Domain;

/// **The learner makes a falsifiable claim about a program, commits to it irrevocably, and then the
/// program runs and settles it.** Reality grades the exam.
///
/// This is `PREDICT` evidence, and it is the one kind of evidence in PRAXIS that **cannot be cheated
/// at all** (I-3) — not because the sandbox is clever, but because of *when* things happen. The
/// learner sees the code and is asked what it will do. They commit an answer **and a mandatory
/// confidence**. Only then does the code run. There is no answer to look up, because **the answer
/// does not exist yet** — it is produced, fresh, by execution, at the moment of resolution.
///
/// ## Why the order is the whole thing
///
/// Every other evidence type has to *defend* an answer key: the oracle hides it (M5.3), the item bank
/// has no column for it (M7.7a), the BUILD harness frames its verdict against a nonce (M9.2). A
/// prediction needs none of that, because **the truth is computed on demand and stored nowhere.** A
/// learner who somehow read every byte of PRAXIS's memory would find no answer to this question,
/// because PRAXIS does not know it either until it runs the code — and by then the learner has
/// already committed.
///
/// The confidence is not optional. **A forecast without a probability is unscoreable** — you cannot
/// tell a well-calibrated learner from a lucky one without it, and calibration (C-6, and M9.4) is the
/// entire point of the Reality Loop. So it is captured at commit time, before the outcome is known,
/// and a prediction that arrives without one is not a prediction.
public static class BehaviourPrediction
{
    /// **Commit.** The learner's claim about the code, and how sure they are — frozen before the code
    /// is run.
    ///
    /// This is a *value*, and it is immutable. There is no `Resolve` method on it, no `SetOutcome`, no
    /// mutable `Correct` field — because the moment a committed prediction could be edited, the
    /// commit-before-resolve guarantee is a suggestion. The outcome lives in a separate object, built
    /// by the resolver, that *references* this one.
    public static Result<CommittedPrediction> Commit(
        PredictionQuestion question,
        string answer,
        double confidence)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return Result<CommittedPrediction>.Invalid(
                "A prediction with no claim is not a prediction. Say what you think the code will do.");
        }

        if (confidence is < 0.0 or > 1.0)
        {
            // **Unscoreable, and said so.** A forecast without a probability cannot be told apart from
            // a guess — and the whole reason this Loop exists is to measure whether you were *right to
            // be as sure as you were*.
            return Result<CommittedPrediction>.Invalid(
                "Confidence must be between 0 and 1. It is not optional: a prediction PRAXIS cannot "
                + "score against how sure you were is a prediction that teaches you nothing about your "
                + "calibration — which is the only reason this exercise exists.");
        }

        return Result<CommittedPrediction>.Success(
            new CommittedPrediction(question.Kind, answer.Trim(), confidence));
    }
}

/// **What the learner is shown, and asked.** The code is visible — they have to reason about it.
/// There is no field here for what it *does*, because that is the thing they are predicting and the
/// thing PRAXIS will not compute until they have committed.
public sealed record PredictionQuestion(
    PredictionKind Kind,

    /// The program, exactly as the learner sees it. **This is not a secret** — the whole task is to
    /// read it and work out what it will do.
    string Code)
{
    /// A one-line reminder of the shape of the answer, per kind. Not the answer — the *form* of it.
    public string Prompt => Kind switch
    {
        PredictionKind.Output => "What will this program print? Give the exact output.",
        PredictionKind.Raises =>
            "Will this program raise an error? Answer 'raises' or 'clean' — and if it raises, name "
            + "the exception type.",
        _ => "Predict what this program will do.",
    };
}

public enum PredictionKind
{
    /// **What will it print?** The learner commits the exact expected stdout.
    Output = 1,

    /// **Will it blow up, and how?** The learner commits whether it raises, and which exception.
    Raises = 2,
}

/// **The commit — frozen, and scoreable.**
public sealed record CommittedPrediction(
    PredictionKind Kind,

    /// What the learner said the code would do. For `Output`, the expected text. For `Raises`, either
    /// `clean`, or `raises` optionally followed by an exception type (`raises ZeroDivisionError`).
    string Answer,

    /// **How sure they were, captured before they found out.** This is what makes the Reality Loop a
    /// calibration gym rather than a quiz.
    double Confidence);

/// **The verdict, produced by running the code.** It references a commit that already existed — the
/// resolver cannot manufacture one, so a resolution *proves* a prior commitment.
public sealed record PredictionResolution(
    bool Correct,

    /// What the program actually did. Now it is safe to know, because the learner has committed.
    string GroundTruth,

    /// Echoed back so the outcome is self-contained: what they said, and how sure they were.
    string Predicted,
    double Confidence)
{
    /// **The Brier score** — `(confidence − outcome)²`, where outcome is 1 if correct and 0 if not.
    ///
    /// This is a fact about *calibration*, not about the answer: a learner who says "90% sure" and is
    /// wrong scores badly not for being wrong, but for having been *confidently* wrong. It is the
    /// number M9.4 turns into the Twin's calibration signal — surfaced here because it is a pure
    /// function of the resolution and belongs with it.
    public double BrierScore
    {
        get
        {
            var outcome = Correct ? 1.0 : 0.0;

            return (Confidence - outcome) * (Confidence - outcome);
        }
    }

    /// **The most useful sentence in the product, when it applies** (C-6). Sure and wrong is the one
    /// the learner most needs to hear, and it is only sayable because the confidence came first.
    public bool SureAndWrong => !Correct && Confidence >= 0.70;
}
