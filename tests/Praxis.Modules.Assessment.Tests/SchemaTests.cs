using Microsoft.EntityFrameworkCore;
using Npgsql;
using Praxis.Modules.Assessment.Persistence;

namespace Praxis.Modules.Assessment.Tests;

/// **The most important test in PRAXIS, and it is four lines of SQL.**
///
/// PRAXIS's central claim is that the tutor *cannot* give you the answer — not that it is instructed
/// not to, not that it is filtered, but that **it cannot**, because the process serving it has no
/// answer in it. A jailbreak retrieves nothing because there is nothing there.
///
/// Every other guarantee in the product is enforced by a type: the verdict has no field a sentence
/// could hide in, the tutor's context has no answer field, the AI-plane contract has no `includeKey`
/// flag. This one is enforced by **the absence of a column in a database**, and so this test asks the
/// database — `information_schema`, the real thing — rather than asking the C# types that claim to
/// describe it.
///
/// ## Why the C# types are not good enough
///
/// A `[NotMapped] public string Answer` would pass any reflection test you like while sitting in
/// memory, in the same process as the tutor, one `ToString()` away from a prompt. And a migration
/// could add `expected_output` next quarter for a perfectly good reason — item authoring, say — and
/// **every existing test would stay green** while PRAXIS's central claim quietly became marketing.
///
/// That is the failure this test exists to make loud. It is deliberately paranoid. It should be.
[Collection(SharedAssessment.Name)]
public sealed class SchemaTests
{
    private readonly AssessmentFixture _fixture;

    public SchemaTests(AssessmentFixture fixture) => _fixture = fixture;

    /// Words that, appearing in a column name in this schema, mean somebody has put the answer where
    /// the tutor's process can reach it.
    ///
    /// `misconception` is *not* on this list and must not be — a named wrong belief is the opposite
    /// of an answer key, and the whole misconception engine (I-6) depends on storing them.
    private static readonly string[] Forbidden =
    [
        "answer",
        "solution",
        "expected",
        "correct_response",
        "correct_option",
        "correct_index",
        "key_index",
        "key_text",
        "answer_key",
        "ground_truth",
        "rubric",
    ];

    [Fact]
    public async Task No_column_in_the_assessment_schema_could_be_an_answer()
    {
        var columns = await ColumnsIn(AssessmentDbContext.Schema);

        Assert.NotEmpty(columns);

        var suspects = columns
            .Where(c => Forbidden.Any(f => c.Column.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            suspects.Count == 0,
            $"""
             A column that looks like an answer has appeared in the `assessment` schema:

                 {string.Join("\n    ", suspects.Select(s => $"{s.Table}.{s.Column}"))}

             PRAXIS's central claim is that the tutor CANNOT give you the answer -- not that it is
             asked not to. That claim holds for exactly one reason: the .NET process serving the tutor
             has no answer in it to leak. The answer went to the oracle's store on the far side of the
             AI-plane wire, and the only thing that ever comes back is one of three words.

             If this column is genuinely needed, it does not belong here. It belongs behind the
             oracle. And if it belongs here, then the tutor can be made to read it -- by a bug, by an
             injection, by a support tool, or by the next person who does not know why this test
             existed.
             """);
    }

    /// Words that, appearing anywhere in an `items` column name, suggest the column might hold or
    /// point at the correct choice. Broader and noisier on purpose than `Forbidden`: this list is not
    /// substring-safe against real column names (real columns here are PascalCase with no
    /// underscores), which is exactly what made the first version of this guard toothless — it banned
    /// `correct_option` when the column that would actually get added is `CorrectOption`.
    ///
    /// This runs only against `items`, so its false positives are cheap: add the column to
    /// `KnownInnocent` below with a one-line reason. Its false negatives are the ones that matter, and
    /// an exact-name blocklist has one for every name nobody thought to type in advance — which is why
    /// this is a "does it brush against a suspicious word" check with an allowlist, not the reverse.
    private static readonly string[] KeyLike =
    [
        "correct", "right", "key", "answer", "index", "option",
    ];

    /// Columns whose names brush against a word in `KeyLike` but are demonstrably not a key.
    /// **Adding to this list is a deliberate act.** That is the point: a new column with a suspicious
    /// name fails the build until somebody consciously argues it is innocent, which is the only way a
    /// guard catches the case nobody thought of.
    private static readonly string[] KnownInnocent =
    [
        "Id", "ConceptId", "FamilyId", "OracleId",  // identifiers, not keys
        "TimesCorrect",                             // a count of attempts, not a key
        "Options",                                  // the choices, which the learner is looking at
        "TargetsMisconception",                     // a named wrong belief -- the opposite of an answer
    ];

    /// The detection logic itself, pulled out of the test so it can be exercised directly against a
    /// hand-picked list of names — the only way to prove it catches a name nobody enumerated, rather
    /// than merely proving the four names somebody thought to write down are absent.
    private static List<string> KeyLikeSuspects(IEnumerable<string> columnNames)
        => columnNames
            .Where(c => KeyLike.Any(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !KnownInnocent.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// **The options are not a secret and the index is.**
    ///
    /// A learner is looking at all four choices; hiding them would be theatre. Which one is right is a
    /// different kind of thing entirely, and it is not here — it went to `praxis_oracle`, a separate
    /// database this process has no connection string for.
    ///
    /// This item is the reason this guard exists in its current, allowlisted shape: options make
    /// "also store which one is right" the single most tempting column to add to this table, ever.
    [Fact]
    public async Task The_item_bank_holds_the_choices_and_never_which_one_is_right()
    {
        var columns = (await ColumnsIn(AssessmentDbContext.Schema))
            .Where(c => c.Table == "items")
            .Select(c => c.Column)
            .ToList();

        Assert.Contains("Passage", columns);
        Assert.Contains("Options", columns);
        Assert.Contains("Format", columns);

        var suspects = KeyLikeSuspects(columns);

        Assert.True(
            suspects.Count == 0,
            $"""
             A column on `items` brushes against a word that suggests it might hold or point at the
             correct choice, and it is not on `KnownInnocent`:

                 {string.Join("\n    ", suspects)}

             Options exist on this item now, which makes "just also store which one is right" the
             single most tempting column to add to this table. If this column is genuinely innocent,
             say so by adding it to `KnownInnocent` with a one-line reason. If it is not innocent, it
             does not belong here — it belongs behind the oracle, in `praxis_oracle`, a database this
             process has no connection string for.
             """);
    }

    /// The bank has a stem, and a stem is **not** a secret. The learner is looking at it.
    [Fact]
    public async Task The_item_bank_holds_the_question_and_nothing_that_answers_it()
    {
        var columns = (await ColumnsIn(AssessmentDbContext.Schema))
            .Where(c => c.Table == "items")
            .Select(c => c.Column)
            .ToList();

        Assert.Contains("Stem", columns);
        Assert.Contains("Difficulty", columns);

        // And the field that makes I-6 possible: which wrong belief this item's distractors catch.
        Assert.Contains("TargetsMisconception", columns);
    }

    /// **There is no `correct` column on the item.** There *is* one on `responses` — which is a
    /// record of what a learner did, not a key to what the right answer was. Those are different
    /// things, and conflating them is how the key gets back in.
    [Fact]
    public async Task Correctness_is_a_fact_about_an_attempt_and_never_a_property_of_an_item()
    {
        var columns = await ColumnsIn(AssessmentDbContext.Schema);

        Assert.DoesNotContain(
            columns,
            c => c.Table == "items"
                 && c.Column.Equals("Correct", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            columns,
            c => c.Table == "responses"
                 && c.Column.Equals("Correct", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<(string Table, string Column)>> ColumnsIn(string schema)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = @schema
            """,
            connection);

        command.Parameters.AddWithValue("schema", schema);

        var found = new List<(string, string)>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            found.Add((reader.GetString(0), reader.GetString(1)));
        }

        return found;
    }
}
