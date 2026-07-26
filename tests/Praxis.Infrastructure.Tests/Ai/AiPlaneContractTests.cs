using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Praxis.Infrastructure.Ai;
using Praxis.Shared;

namespace Praxis.Infrastructure.Tests.Ai;

/// **The two languages cannot silently disagree.**
///
/// `contracts/ai-plane.openapi.json` is written by the Python service (and pinned by a test on that
/// side, so it cannot go stale). This test reads the same file and checks the C# records against it.
///
/// The roadmap asked for a *generated* client. This is a written one plus a drift test, and the
/// trade is deliberate: codegen gives you a guarantee you cannot read, and every guarantee in this
/// system needs a reviewer to be able to look at it and see that it holds. The property is the same
/// — a wire-format change that C# has not been told about fails a build rather than reaching a
/// learner — and *this* file is short enough to audit, which an NSwag output is not.
public sealed class AiPlaneContractTests
{
    private static readonly JsonDocument Contract = LoadContract();

    private static JsonElement Schemas => Contract.RootElement
        .GetProperty("components").GetProperty("schemas");

    // ── The three things the core may ask for ────────────────────────────────

    [Fact]
    public void The_core_can_reach_exactly_the_endpoints_the_contract_offers()
    {
        var paths = Contract.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/oracle/check-claim", paths);
        Assert.Contains("/assessment/grade-by-concept", paths);
        Assert.Contains("/assessment/items", paths);
        Assert.Contains("/tutor/turn", paths);
        Assert.Contains("/tutor/turn/stream", paths);

        // The superseded rubric-carrying door is gone: the core no longer holds a rubric to send.
        Assert.DoesNotContain("/assessment/grade", paths);

        // And the client exposes one method per capability, and no more. A fourth method here means
        // somebody added a route without anyone reading this file.
        var methods = typeof(IAiPlane).GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(IAiPlane.CheckClaimAsync),
                nameof(IAiPlane.GradeByConceptAsync),
                nameof(IAiPlane.GenerateItemAsync),
                nameof(IAiPlane.TutorTurnAsync),
                nameof(IAiPlane.TutorTurnStreamAsync),
            },
            methods);
    }

    // ── Drift ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(CheckClaimRequest), "CheckClaimRequest")]
    [InlineData(typeof(CheckClaimResponse), "CheckClaimResponse")]
    [InlineData(typeof(GradeByConceptRequest), "GradeByConceptRequest")]
    [InlineData(typeof(GradeResponse), "GradeResponse")]
    [InlineData(typeof(GenerateItemRequest), "GenerateItemRequest")]
    [InlineData(typeof(ItemResponse), "ItemResponse")]
    [InlineData(typeof(TutorTurnRequest), "TutorTurnRequest")]
    [InlineData(typeof(TutorTurnResponse), "TutorTurnResponse")]
    [InlineData(typeof(TurnSpec), "TurnSpec")]
    public void Every_property_on_the_csharp_record_exists_in_the_contract(Type type, string schema)
    {
        var declared = Schemas.GetProperty(schema).GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.True(
                declared.Contains(SnakeCase(property.Name)),
                $"{type.Name}.{property.Name} is not in the contract. Either the AI plane never "
                + "sends it — in which case it is always null and something is quietly broken — or "
                + "somebody added it here and forgot the other side of the wire.");
        }
    }

    [Theory]
    [InlineData(typeof(CheckClaimResponse), "CheckClaimResponse")]
    [InlineData(typeof(GradeResponse), "GradeResponse")]
    [InlineData(typeof(ItemResponse), "ItemResponse")]
    [InlineData(typeof(TutorTurnResponse), "TutorTurnResponse")]
    public void Every_field_the_contract_returns_is_read_by_the_csharp_record(Type type, string schema)
    {
        // The other direction, and the one that actually bites: the AI plane starts sending a field
        // the core does not read, so a real signal — an abstention, a cost, a misconception — is
        // silently dropped on the floor and nobody notices for a year.
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => SnakeCase(p.Name))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in Schemas.GetProperty(schema).GetProperty("properties").EnumerateObject())
        {
            Assert.True(
                properties.Contains(field.Name),
                $"The AI plane returns {schema}.{field.Name} and the core does not read it. "
                + "Whatever that field was telling us about a learner, it is being dropped.");
        }
    }

    /// **Pinned deliberately, not just caught by reflection.** The reflection-based drift tests above
    /// would catch it if either side forgot its half of the wire — but they would catch it just as
    /// happily if somebody renamed both halves to something wrong *together*. This test names the two
    /// fields the plan actually asked for, so a reviewer reading the test can see the wire's shape
    /// without cross-referencing the OpenAPI file: the core declares wrong beliefs out
    /// (`known_misconceptions`), and the only thing that can ever come back naming one is a key from
    /// that same declared set (`targets_misconception`) — never free text.
    [Fact]
    public void The_wire_carries_declared_wrong_beliefs_out_and_a_hunted_key_back()
    {
        var request = Schemas.GetProperty("GenerateItemRequest").GetProperty("properties");
        Assert.True(request.TryGetProperty("known_misconceptions", out _));

        var response = Schemas.GetProperty("ItemResponse").GetProperty("properties");
        Assert.True(response.TryGetProperty("targets_misconception", out _));

        Assert.Contains(
            nameof(GenerateItemRequest.KnownMisconceptions),
            typeof(GenerateItemRequest).GetProperties().Select(p => p.Name));

        Assert.Contains(
            nameof(ItemResponse.TargetsMisconception),
            typeof(ItemResponse).GetProperties().Select(p => p.Name));
    }

    /// **Pinned deliberately, Task 6 (M17).** The reflection-based drift tests above would catch it
    /// if either side forgot its half of the wire — but a reviewer should not have to reconstruct
    /// "what did this milestone actually add" from a diff of two hundred lines of JSON. This test
    /// names the two fields the plan asked for: a fixed-key item's passage and options come back on
    /// `ItemResponse`, and — deliberately absent from every assertion below — no index of which one
    /// is correct ever does.
    [Fact]
    public void A_fixed_key_item_returns_a_passage_and_options_and_no_key()
    {
        var response = Schemas.GetProperty("ItemResponse").GetProperty("properties");
        Assert.True(response.TryGetProperty("passage", out _));
        Assert.True(response.TryGetProperty("options", out _));

        Assert.Contains(
            nameof(ItemResponse.Passage),
            typeof(ItemResponse).GetProperties().Select(p => p.Name));

        Assert.Contains(
            nameof(ItemResponse.Options),
            typeof(ItemResponse).GetProperties().Select(p => p.Name));
    }

    /// **The other half of the wire.** The core can now ask for a fixed-key item
    /// (`GenerateItemRequest.item_format`) and submit a choice instead of a claim
    /// (`CheckClaimRequest.selected_option`). Neither has a C# property yet — sending them is Task
    /// 7/8's job — but the *contract* must offer both today, or those tasks have nothing to bind to.
    [Fact]
    public void The_contract_offers_a_format_in_and_a_selection_back()
    {
        var itemRequest = Schemas.GetProperty("GenerateItemRequest").GetProperty("properties");
        Assert.True(itemRequest.TryGetProperty("item_format", out _));

        var checkClaim = Schemas.GetProperty("CheckClaimRequest").GetProperty("properties");
        Assert.True(checkClaim.TryGetProperty("selected_option", out _));

        // `claim` is no longer required — a fixed-key answer is a selection, not words.
        var required = Schemas.GetProperty("CheckClaimRequest").GetProperty("required")
            .EnumerateArray()
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("claim", required);
    }

    // ── The refusals, at the seam ────────────────────────────────────────────

    /// **The most dangerous place in the system for an `answer` field.**
    ///
    /// Here it would be a C# property *and* a JSON field — present on both sides of the wire,
    /// perfectly typed, and irresistible. The tutor is a program with an HTTP client: anything the
    /// contract exposes, it can call.
    [Fact]
    public void Nothing_in_the_contract_or_the_client_can_carry_an_answer()
    {
        string[] forbidden = ["answer", "answer_key", "correct_answer", "solution", "expected"];

        foreach (var schema in Schemas.EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var field in properties.EnumerateObject())
            {
                Assert.DoesNotContain(field.Name, forbidden, StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (var type in typeof(IAiPlane).Assembly.GetTypes()
                     .Where(t => t.Namespace == typeof(IAiPlane).Namespace))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain(
                    SnakeCase(property.Name), forbidden, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// **R-1 crosses the wire.** If `pending` were not in the enum the core is compiled against, it
    /// would have no way to represent "PRAXIS does not know" — and it would have to pick. It would
    /// pick `passed`, on the day the queue was long and it was probably fine.
    [Fact]
    public void Pending_survives_the_journey_from_python_to_csharp()
    {
        var declared = Schemas.GetProperty("GradeOutcome").GetProperty("enum")
            .EnumerateArray()
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "passed", "failed", "pending" },
            declared);

        Assert.Equal(
            declared,
            Enum.GetNames<GradeOutcome>().Select(SnakeCase).ToHashSet(StringComparer.Ordinal));
    }

    /// **Pinned exactly, Task 7 (M17).** The reflection-based drift tests above check property
    /// *names* only — nothing here catches a renamed enum *member*. `ItemFormat.MultipleChoice`
    /// becoming, say, `ItemFormat.FixedKey` compiles clean, breaks nothing else in this file, and
    /// every other existing test stays green. The AI plane then 422s every generate call for a
    /// fixed-key concept, and `AiPlaneClient` turns that into a `Result.Invalid` rather than a
    /// crash — loud eventually, silent in CI.
    ///
    /// These two strings are a contract with a Python `StrEnum` (`ai/src/praxis_ai/items.py`) on the
    /// far side of a network hop, so this test does not inspect a name — it actually serializes and
    /// deserializes through the converter `AiPlaneClient` uses (`JsonStringEnumConverter` with
    /// `JsonNamingPolicy.SnakeCaseLower`) and pins the exact wire strings both languages agree on.
    [Fact]
    public void ItemFormat_serializes_and_round_trips_the_strings_the_ai_plane_expects()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        };

        Assert.Equal("\"prose\"", JsonSerializer.Serialize(ItemFormat.Prose, options));
        Assert.Equal("\"multiple_choice\"", JsonSerializer.Serialize(ItemFormat.MultipleChoice, options));

        Assert.Equal(ItemFormat.Prose, JsonSerializer.Deserialize<ItemFormat>("\"prose\"", options));
        Assert.Equal(
            ItemFormat.MultipleChoice, JsonSerializer.Deserialize<ItemFormat>("\"multiple_choice\"", options));
    }

    /// The AI plane is stateless. It cannot know who is calling unless the call says so — and
    /// nothing is spent anonymously.
    [Theory]
    [InlineData("CheckClaimRequest")]
    [InlineData("GradeByConceptRequest")]
    [InlineData("GenerateItemRequest")]
    [InlineData("TutorTurnRequest")]
    public void Every_request_names_the_learner_it_is_spending_on(string schema)
    {
        var required = Schemas.GetProperty(schema).GetProperty("required")
            .EnumerateArray()
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("learner_id", required);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static string SnakeCase(string name)
        => string.Concat(name.Select((c, i) =>
            char.IsUpper(c) ? (i > 0 ? "_" : string.Empty) + char.ToLowerInvariant(c) : c.ToString()));

    private static JsonDocument LoadContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "contracts")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new InvalidOperationException(
                "contracts/ai-plane.openapi.json was not found. Run: "
                + "cd ai && uv run python scripts/export_contract.py");
        }

        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, "contracts", "ai-plane.openapi.json")));
    }
}
