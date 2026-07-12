using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Praxis.Api.Tests.Probes;
using Praxis.Shared;

namespace Praxis.Api.Tests;

/// The host's promises, driven through real HTTP:
///   - it is alive
///   - invalid input never reaches a handler
///   - a Result comes back in the standard envelope, with the right status code
///   - a bug becomes a 500 with a correlation id, and never leaks a stack trace
[Collection(SharedApi.Name)]
public sealed class HostPipelineTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PraxisApiFactory _factory;

    public HostPipelineTests(ApiFixture api) => _factory = api.Factory;

    [Fact]
    public async Task Health_reports_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_command_reaches_the_handler_and_comes_back_in_the_envelope()
    {
        using var client = _factory.CreateClient();

        using var response = await Post(client, new ProbeSlice.Command("compile", 25));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Read<ProbeResponse>(response);

        Assert.True(body.Success);
        Assert.Equal("Probe accepted.", body.Message);
        Assert.Equal("compile", body.Data?.Name);
        Assert.Equal(25, body.Data?.Minutes);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_before_the_handler_runs()
    {
        using var client = _factory.CreateClient();

        // Empty name and a non-positive duration: two rules, so two messages.
        using var response = await Post(client, new ProbeSlice.Command("", 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await Read<object>(response);

        Assert.False(body.Success);
        Assert.Equal("Validation failed.", body.Message);
        Assert.Contains("Name is required.", body.Errors!, StringComparer.Ordinal);
        Assert.Contains("Minutes must be positive.", body.Errors!, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    public async Task Expected_failures_map_to_their_status_codes(string name, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient();

        using var response = await Post(client, new ProbeSlice.Command(name, 10));

        Assert.Equal(expected, response.StatusCode);

        var body = await Read<object>(response);
        Assert.False(body.Success);
    }

    [Fact]
    public async Task A_bug_becomes_a_500_with_a_correlation_id_and_no_stack_trace()
    {
        using var client = _factory.CreateClient();

        using var response = await Post(client, new ProbeSlice.Command("boom", 10));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        // The learner is never shown our internals.
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("at Praxis.", raw, StringComparison.Ordinal);

        var body = JsonSerializer.Deserialize<ApiResponse<object>>(raw, Json);

        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("An unexpected error occurred.", body.Message);
        Assert.Contains(body.Errors!, e => e.StartsWith("Correlation id:", StringComparison.Ordinal));
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, ProbeSlice.Command command)
        => client.PostAsJsonAsync(new Uri("/api/probe/slices", UriKind.Relative), command, Json);

    private static async Task<ApiResponse<T>> Read<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json))!;
}
