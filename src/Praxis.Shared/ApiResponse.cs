namespace Praxis.Shared;

/// The one HTTP envelope. Every endpoint in PRAXIS returns this shape, so the web client
/// has exactly one thing to unwrap.
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string? message, IReadOnlyList<string>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}
