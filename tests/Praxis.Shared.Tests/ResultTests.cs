namespace Praxis.Shared.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_carries_its_value()
    {
        var result = Result<int>.Success(42, "done");

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal(42, result.Value);
        Assert.Equal("done", result.Message);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(ResultStatus.NotFound)]
    [InlineData(ResultStatus.Invalid)]
    [InlineData(ResultStatus.Conflict)]
    [InlineData(ResultStatus.Forbidden)]
    [InlineData(ResultStatus.Unauthorized)]
    public void Failures_are_not_successes(ResultStatus status)
    {
        var result = Failure<string>(status);

        Assert.False(result.IsSuccess);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Reading_the_value_off_a_failure_is_a_bug_and_says_so()
    {
        var result = Result<string>.NotFound("no such concept");

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);

        Assert.Contains("NotFound", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IsSuccess", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_carries_the_individual_errors()
    {
        var result = Result<string>.Invalid("Validation failed.", ["Code is required.", "Name is too long."]);

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Code is required.", result.Errors, StringComparer.Ordinal);
    }

    [Fact]
    public void Errors_default_to_empty_never_null()
    {
        // Callers enumerate Errors without a null check. That must always be safe.
        Assert.Empty(Result<string>.NotFound("gone").Errors);
        Assert.Empty(Result.Success().Errors);
    }

    private static Result<string> Failure<T>(ResultStatus status) => status switch
    {
        ResultStatus.NotFound => Result<string>.NotFound("x"),
        ResultStatus.Invalid => Result<string>.Invalid("x"),
        ResultStatus.Conflict => Result<string>.Conflict("x"),
        ResultStatus.Forbidden => Result<string>.Forbidden("x"),
        ResultStatus.Unauthorized => Result<string>.Unauthorized("x"),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
