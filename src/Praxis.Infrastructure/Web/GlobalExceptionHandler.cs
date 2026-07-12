using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Praxis.Shared;

namespace Praxis.Infrastructure.Web;

/// The last line of defence. If we are here, something is genuinely broken — a bug or an
/// infrastructure fault — because every *expected* failure came back as a Result.
///
/// The caller gets a correlation id and nothing else. Stack traces are for the log.
public sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = httpContext.TraceIdentifier;

        UnhandledException(
            _logger,
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId,
            exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(
                "An unexpected error occurred.",
                [$"Correlation id: {correlationId}"]),
            cancellationToken);

        return true;
    }

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Method} {Path}. Correlation {CorrelationId}.")]
    private static partial void UnhandledException(
        ILogger logger, string method, string path, string correlationId, Exception exception);
}
