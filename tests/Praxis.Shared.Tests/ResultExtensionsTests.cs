using Microsoft.AspNetCore.Mvc;

namespace Praxis.Shared.Tests;

/// This mapping is the contract between every handler and every HTTP client we will ever
/// write. If it drifts, the web app starts guessing.
public sealed class ResultExtensionsTests
{
    [Fact]
    public void Success_maps_to_200_with_the_value()
    {
        var http = Result<string>.Success("held", "Concept held.").ToApiResponse();

        var ok = Assert.IsType<OkObjectResult>(http);
        var body = Assert.IsType<ApiResponse<string>>(ok.Value);

        Assert.True(body.Success);
        Assert.Equal("held", body.Data);
        Assert.Equal("Concept held.", body.Message);
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, 404)]
    [InlineData(ResultStatus.Invalid, 400)]
    [InlineData(ResultStatus.Conflict, 409)]
    [InlineData(ResultStatus.Forbidden, 403)]
    [InlineData(ResultStatus.Unauthorized, 401)]
    public void Each_failure_maps_to_its_status_code(ResultStatus status, int expectedCode)
    {
        var http = Failure(status).ToApiResponse();

        Assert.Equal(expectedCode, StatusCodeOf(http));
    }

    [Fact]
    public void A_failure_never_reports_success_in_the_envelope()
    {
        var http = Result<string>.Conflict("Already exists.").ToApiResponse();

        var body = Assert.IsType<ApiResponse<string>>(((ObjectResult)http).Value);

        Assert.False(body.Success);
        Assert.Null(body.Data);
        Assert.Equal("Already exists.", body.Message);
    }

    [Fact]
    public void Validation_errors_survive_the_mapping()
    {
        var http = Result<string>.Invalid("Validation failed.", ["Code is required."]).ToApiResponse();

        var body = Assert.IsType<ApiResponse<string>>(((ObjectResult)http).Value);

        Assert.Equal(["Code is required."], body.Errors);
    }

    [Fact]
    public void The_non_generic_overload_maps_the_same_way()
    {
        Assert.Equal(200, StatusCodeOf(Result.Success().ToApiResponse()));
        Assert.Equal(404, StatusCodeOf(Result.NotFound("gone").ToApiResponse()));
        Assert.Equal(409, StatusCodeOf(Result.Conflict("dupe").ToApiResponse()));
    }

    private static int StatusCodeOf(IActionResult result) => result switch
    {
        OkObjectResult r => r.StatusCode ?? 200,
        NotFoundObjectResult r => r.StatusCode ?? 404,
        BadRequestObjectResult r => r.StatusCode ?? 400,
        ConflictObjectResult r => r.StatusCode ?? 409,
        ObjectResult r => r.StatusCode ?? 0,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unmapped IActionResult."),
    };

    private static Result<string> Failure(ResultStatus status) => status switch
    {
        ResultStatus.NotFound => Result<string>.NotFound("x"),
        ResultStatus.Invalid => Result<string>.Invalid("x"),
        ResultStatus.Conflict => Result<string>.Conflict("x"),
        ResultStatus.Forbidden => Result<string>.Forbidden("x"),
        ResultStatus.Unauthorized => Result<string>.Unauthorized("x"),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
