using Microsoft.AspNetCore.Mvc;

namespace Praxis.Shared;

/// The only place a Result becomes an HTTP response. Controllers call this and nothing else —
/// if status-code logic ever appears in a controller, it belongs here instead.
public static class ResultExtensions
{
    public static IActionResult ToApiResponse<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            ResultStatus.Success => new OkObjectResult(
                ApiResponse<T>.Ok(result.Value, result.Message)),

            ResultStatus.NotFound => new NotFoundObjectResult(
                ApiResponse<T>.Fail(result.Message, result.Errors)),

            ResultStatus.Invalid => new BadRequestObjectResult(
                ApiResponse<T>.Fail(result.Message, result.Errors)),

            ResultStatus.Conflict => new ConflictObjectResult(
                ApiResponse<T>.Fail(result.Message, result.Errors)),

            ResultStatus.Forbidden => new ObjectResult(
                ApiResponse<T>.Fail(result.Message, result.Errors)) { StatusCode = 403 },

            ResultStatus.Unauthorized => new ObjectResult(
                ApiResponse<T>.Fail(result.Message, result.Errors)) { StatusCode = 401 },

            _ => new ObjectResult(
                ApiResponse<T>.Fail(result.Message)) { StatusCode = 500 },
        };
    }

    /// For writes that return nothing meaningful. Same mapping, empty body on success.
    public static IActionResult ToApiResponse(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            ResultStatus.Success => new OkObjectResult(
                ApiResponse<object>.Ok(new { }, result.Message)),

            ResultStatus.NotFound => new NotFoundObjectResult(
                ApiResponse<object>.Fail(result.Message, result.Errors)),

            ResultStatus.Invalid => new BadRequestObjectResult(
                ApiResponse<object>.Fail(result.Message, result.Errors)),

            ResultStatus.Conflict => new ConflictObjectResult(
                ApiResponse<object>.Fail(result.Message, result.Errors)),

            ResultStatus.Forbidden => new ObjectResult(
                ApiResponse<object>.Fail(result.Message, result.Errors)) { StatusCode = 403 },

            ResultStatus.Unauthorized => new ObjectResult(
                ApiResponse<object>.Fail(result.Message, result.Errors)) { StatusCode = 401 },

            _ => new ObjectResult(
                ApiResponse<object>.Fail(result.Message)) { StatusCode = 500 },
        };
    }
}
