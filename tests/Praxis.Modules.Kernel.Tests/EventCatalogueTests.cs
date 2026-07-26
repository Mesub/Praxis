using Praxis.Modules.Kernel.Domain;
using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Kernel.Tests;

public sealed class EventCatalogueTests
{
    /// **The refusal that defines the product, as a test.**
    ///
    /// PRAXIS has no "mark as complete" button. That is not a UI decision someone can revisit in
    /// a design review — it is the absence of any event that could record such a thing. Mastery
    /// is only ever inferred from evidence, and it decays.
    ///
    /// If this test ever goes red, someone has not added a feature. They have deleted the product,
    /// and the diff that did it should not merge (CANON §0).
    [Theory]
    [InlineData("Complete")]
    [InlineData("Completed")]
    [InlineData("Finish")]
    [InlineData("Finished")]
    [InlineData("Done")]
    [InlineData("Learned")]
    [InlineData("Mastered")]
    [InlineData("Passed")]
    [InlineData("Graduated")]
    [InlineData("Marked")]
    public void No_event_can_declare_a_concept_finished(string forbidden)
    {
        var offenders = Enum.GetNames<LearningEventType>()
            .Where(name => PascalWords(name).Contains(forbidden, StringComparer.OrdinalIgnoreCase))

            // A *session* may end — you closed the laptop. Knowledge may not; that is the
            // distinction the entire product turns on.
            .Where(name => name != nameof(LearningEventType.SessionCompleted))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"LearningEventType contains {string.Join(", ", offenders)}. There is no completion in "
            + "PRAXIS. Mastery is inferred from evidence and it decays — it cannot be declared, "
            + "not by the learner and not by us. See CANON §0.");
    }

    /// Whole words only. "SessionAbandoned" contains the letters of "done", and a substring
    /// match would have us fail a test over English rather than over the domain.
    private static string[] PascalWords(string name)
        => System.Text.RegularExpressions.Regex.Matches(name, "[A-Z][a-z]*")
            .Select(m => m.Value)
            .ToArray();

    [Fact]
    public void Every_event_type_has_exactly_one_payload()
    {
        // An event no projection can read is an event that gets written and then silently ignored.
        EventCatalogue.EveryEventTypeHasExactlyOnePayload();
    }

    [Fact]
    public void Every_payload_round_trips_through_the_log()
    {
        var learner = Guid.CreateVersion7();
        var occurred = DateTime.UtcNow;

        var original = new AnswerGiven(
            SessionId: Guid.CreateVersion7(),
            ItemId: Guid.CreateVersion7(),
            EvidenceType: EvidenceType.Apply,
            Correct: true,
            StatedConfidence: 0.7,
            Seconds: 94);

        var e = LearningEvent.Create(
            learner,
            AnswerGiven.EventType,
            EventCatalogue.Serialize(original),
            occurred,
            occurred);

        var rehydrated = EventCatalogue.Deserialize<AnswerGiven>(e);

        Assert.Equal(original, rehydrated);
    }

    /// **The test a round trip of the *current* shape can never be.**
    ///
    /// `Every_payload_round_trips_through_the_log` above proves the current `AnswerGiven` shape
    /// survives a round trip — but it constructs the record, serializes it, and deserializes what
    /// it just wrote. That can never catch a change that breaks replay of a row that is *already on
    /// the log*, because it never touches a byte the current code did not just produce itself.
    ///
    /// The Twin is a projection of the event log and must be rebuildable from zero (CLAUDE.md) — so
    /// a payload change that quietly breaks deserialization of an old row does not fail loudly, it
    /// invalidates every learner's history the next time somebody runs a rebuild. The only way to
    /// catch that is to hand-write the *old* shape and deserialize it through the exact path a
    /// projection uses, not to trust that today's record still agrees with yesterday's.
    ///
    /// `SessionStarted` gained `ItemId` (M17), last and defaulted, specifically so pre-M17 rows
    /// (`sessionId` and `plannedMinutes` only — no `itemId` key at all) keep deserializing. This is
    /// that proof. It is deliberately not about `SessionStarted` in particular: it is the *shape* of
    /// test any future payload change needs, because nothing else in this file — or anywhere else —
    /// would notice if a payload change broke old rows instead of merely being safe for new ones.
    [Fact]
    public void A_pre_M17_SessionStarted_with_no_ItemId_still_replays()
    {
        var learner = Guid.CreateVersion7();
        var occurred = DateTime.UtcNow;
        var sessionId = Guid.CreateVersion7();

        // The literal pre-M17 shape. Not built by serializing a `SessionStarted` and stripping a
        // field — that would only prove today's serializer agrees with itself. This is the JSON a
        // pre-M17 writer actually put in the log.
        var legacyJson = $$"""{"sessionId":"{{sessionId}}","plannedMinutes":15}""";

        var e = LearningEvent.Create(
            learner,
            SessionStarted.EventType,
            legacyJson,
            occurred,
            occurred);

        // The same path a projection reads through — not a hand-rolled JsonSerializerOptions that
        // might quietly diverge from the one the log is actually read with.
        var rehydrated = EventCatalogue.Deserialize<SessionStarted>(e);

        Assert.Equal(sessionId, rehydrated.SessionId);
        Assert.Equal(15, rehydrated.PlannedMinutes);

        // Null, honestly — not Guid.Empty standing in for "we don't know". A row written before
        // M17 genuinely predates the concept of a bound item.
        Assert.Null(rehydrated.ItemId);
    }

    [Fact]
    public void A_payload_knows_which_event_it_is()
    {
        Assert.Equal(LearningEventType.EvidenceAccepted, EvidenceAccepted.EventType);
        Assert.Equal(LearningEventType.SessionAbandoned, SessionAbandoned.EventType);
        Assert.Equal(LearningEventType.PredictionResolved, PredictionResolved.EventType);
    }

    /// The six kinds of proof (I-2). They are deliberately not equal in what they can prove:
    /// EXPLAIN is graded by a model and is the weakest; APPLY, BUILD and PREDICT cannot be
    /// fabricated by a model, and only they can push a concept into HELD (C-4, enforced in M3.5).
    [Fact]
    public void There_are_exactly_six_kinds_of_evidence()
    {
        var types = Enum.GetValues<EvidenceType>();

        Assert.Equal(6, types.Length);
        Assert.Contains(EvidenceType.Apply, types);
        Assert.Contains(EvidenceType.Build, types);
        Assert.Contains(EvidenceType.Predict, types);
        Assert.Contains(EvidenceType.Explain, types);
        Assert.Contains(EvidenceType.Recall, types);
        Assert.Contains(EvidenceType.Critique, types);
    }

    /// The last rung of the hint ladder is not "here is the answer" — it is the smallest scaffold
    /// that still leaves the learner something to produce (I-5).
    [Fact]
    public void The_hint_ladder_has_five_rungs_and_none_of_them_is_the_answer()
    {
        var rungs = Enum.GetNames<HintRung>();

        Assert.Equal(5, rungs.Length);
        Assert.DoesNotContain("Answer", rungs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Solution", rungs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reveal", rungs, StringComparer.OrdinalIgnoreCase);
    }
}
