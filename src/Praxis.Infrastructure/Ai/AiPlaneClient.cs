using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Praxis.Shared;

namespace Praxis.Infrastructure.Ai;

/// The typed client. **The only way anything in PRAXIS talks to a language model.**
///
/// Every engine that wants an LLM goes through this interface, which goes through this contract,
/// which goes through the AI plane — where the model is chosen by the task, the call is attributed
/// to a learner, and the answer is never in the tutor's context. A slice that new'd up an
/// `HttpClient` and called Anthropic directly would bypass all three, so it is worth saying plainly:
/// **do not do that.**
public sealed partial class AiPlaneClient : IAiPlane
{
    private readonly HttpClient _http;
    private readonly ILogger<AiPlaneClient> _log;

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI plane is unreachable.")]
    private partial void LogUnreachable(Exception cause);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AiPlaneClient(HttpClient http, ILogger<AiPlaneClient> log) => (_http, _log) = (http, log);

    public Task<Result<CheckClaimResponse>> CheckClaimAsync(
        CheckClaimRequest request, CancellationToken ct = default)
        => PostAsync<CheckClaimRequest, CheckClaimResponse>("oracle/check-claim", request, ct);

    public Task<Result<GradeResponse>> GradeByConceptAsync(
        GradeByConceptRequest request, CancellationToken ct = default)
        => PostAsync<GradeByConceptRequest, GradeResponse>("assessment/grade-by-concept", request, ct);

    public Task<Result<ItemResponse>> GenerateItemAsync(
        GenerateItemRequest request, CancellationToken ct = default)
        => PostAsync<GenerateItemRequest, ItemResponse>("assessment/items", request, ct);

    public Task<Result<TutorTurnResponse>> TutorTurnAsync(
        TutorTurnRequest request, CancellationToken ct = default)
        => PostAsync<TutorTurnRequest, TutorTurnResponse>("tutor/turn", request, ct);

    /// Server-sent events, relayed. **The bytes are not inspected, rewritten, or buffered** — they
    /// are passed through, because there is nothing in them to filter and pretending otherwise would
    /// imply that filtering was the guarantee. It is not. The absence of the answer is.
    public async IAsyncEnumerable<string> TutorTurnStreamAsync(
        TutorTurnRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "tutor/turn/stream")
        {
            Content = JsonContent.Create(request, options: Json),
        };

        using var response = await _http.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            // **PRAXIS does not fill the silence.** No invented hint to keep the screen moving.
            yield return "The tutor is unreachable right now. PRAXIS will not invent a hint to fill "
                         + "the silence — your budget has not been spent on nothing, and your work "
                         + "is not lost.";

            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data: ".Length..];

            if (payload == "[DONE]")
            {
                yield break;
            }

            var chunk = JsonSerializer.Deserialize<StreamChunk>(payload, Json);

            if (chunk?.Text is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    private sealed record StreamChunk(string? Text, string? Error);

    private async Task<Result<TOut>> PostAsync<TIn, TOut>(
        string path, TIn body, CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            response = await _http.PostAsJsonAsync(path, body, Json, ct);
        }
        catch (HttpRequestException down)
        {
            // **The AI plane is down, and PRAXIS says so.** It does not fall back to a cheaper model
            // it happens to have a key for, and it does not invent a grade. A learner whose work
            // cannot be marked right now is told that; a learner whose work is marked by a guess is
            // lied to, and finds out months later.
            LogUnreachable(down);

            return Result<TOut>.Invalid(
                "The AI plane is unreachable. PRAXIS cannot mark this right now, and it will not "
                + "guess. Your work is not lost.");
        }

        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<TOut>(Json, ct);

            return value is null
                ? Result<TOut>.Invalid("The AI plane returned an empty body.")
                : Result<TOut>.Success(value);
        }

        var detail = await DetailAsync(response, ct);

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => Result<TOut>.NotFound(detail),

            // 422 from the oracle means it could not read the model's verdict. **It does not mean
            // the learner was wrong**, and the caller must not treat it as such.
            HttpStatusCode.UnprocessableContent => Result<TOut>.Invalid(detail),

            HttpStatusCode.ServiceUnavailable => Result<TOut>.Invalid(detail),

            _ => Result<TOut>.Invalid($"The AI plane returned {(int)response.StatusCode}: {detail}"),
        };
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Detail>(Json, ct);

            return problem?.Value ?? response.ReasonPhrase ?? "no detail";
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "no detail";
        }
    }

    private sealed record Detail([property: JsonPropertyName("detail")] string? Value);
}

public static class AiPlaneModule
{
    public static IServiceCollection AddAiPlane(this IServiceCollection services, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<IAiPlane, AiPlaneClient>(http =>
        {
            http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            // Three Opus judges and an adjudicator is not a fast operation, and cutting it off at
            // 30s would turn a slow grade into a failed one.
            http.Timeout = TimeSpan.FromMinutes(3);
        });

        return services;
    }
}
