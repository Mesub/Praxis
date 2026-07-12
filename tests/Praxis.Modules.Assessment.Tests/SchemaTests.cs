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
