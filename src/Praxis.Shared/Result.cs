namespace Praxis.Shared;

/// What happened, as a value rather than as an exception.
///
/// Expected failures — not found, invalid, conflicting, forbidden — are outcomes a caller
/// should reason about, so they are returned. Exceptions are reserved for bugs and
/// infrastructure faults, and the global handler catches those.
public enum ResultStatus
{
    Success,
    NotFound,
    Invalid,
    Conflict,
    Forbidden,
    Unauthorized,
}

public class Result
{
    public ResultStatus Status { get; }
    public string? Message { get; }
    public IReadOnlyList<string> Errors { get; }

    public bool IsSuccess => Status == ResultStatus.Success;

    protected Result(ResultStatus status, string? message, IReadOnlyList<string>? errors)
        => (Status, Message, Errors) = (status, message, errors ?? []);

    public static Result Success(string? message = null)
        => new(ResultStatus.Success, message, null);

    public static Result NotFound(string message)
        => new(ResultStatus.NotFound, message, null);

    public static Result Invalid(string message, IReadOnlyList<string>? errors = null)
        => new(ResultStatus.Invalid, message, errors);

    public static Result Conflict(string message)
        => new(ResultStatus.Conflict, message, null);

    public static Result Forbidden(string message)
        => new(ResultStatus.Forbidden, message, null);

    public static Result Unauthorized(string message)
        => new(ResultStatus.Unauthorized, message, null);

    /// Re-type a failure without flattening what kind of failure it was.
    ///
    /// A slice that calls a service and maps its failure by hand will, sooner or later, turn every
    /// one of them into a 400 — and then a learner with no objective is told their *request* was
    /// malformed, which is both wrong and unhelpful. The status is carried, so the shape of the
    /// refusal survives the trip to the client.
    public Result<TOut> CarryFailure<TOut>()
    {
        if (IsSuccess)
        {
            throw new InvalidOperationException(
                "CarryFailure was called on a successful result. There is no failure to carry, and "
                + "the value would be silently dropped.");
        }

        return Status switch
        {
            ResultStatus.NotFound => Result<TOut>.NotFound(Message!),
            ResultStatus.Conflict => Result<TOut>.Conflict(Message!),
            ResultStatus.Forbidden => Result<TOut>.Forbidden(Message!),
            ResultStatus.Unauthorized => Result<TOut>.Unauthorized(Message!),
            _ => Result<TOut>.Invalid(Message!, Errors),
        };
    }
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    /// Only meaningful on success. Reading it off a failure is a bug, and says so.
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read Value from a {Status} result. Check IsSuccess first.");

    private Result(T value, string? message)
        : base(ResultStatus.Success, message, null) => _value = value;

    private Result(ResultStatus status, string? message, IReadOnlyList<string>? errors)
        : base(status, message, errors) { }

    public static Result<T> Success(T value, string? message = null)
        => new(value, message);

    public static new Result<T> NotFound(string message)
        => new(ResultStatus.NotFound, message, null);

    public static new Result<T> Invalid(string message, IReadOnlyList<string>? errors = null)
        => new(ResultStatus.Invalid, message, errors);

    public static new Result<T> Conflict(string message)
        => new(ResultStatus.Conflict, message, null);

    public static new Result<T> Forbidden(string message)
        => new(ResultStatus.Forbidden, message, null);

    public static new Result<T> Unauthorized(string message)
        => new(ResultStatus.Unauthorized, message, null);
}
