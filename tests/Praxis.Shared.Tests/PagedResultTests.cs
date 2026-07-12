namespace Praxis.Shared.Tests;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(100, 20, 5)]
    [InlineData(101, 20, 6)]   // the remainder gets its own page
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    public void Total_pages_rounds_up(int total, int pageSize, int expectedPages)
    {
        var page = new PagedResult<string>([], total, 1, pageSize);

        Assert.Equal(expectedPages, page.TotalPages);
    }

    [Fact]
    public void A_zero_page_size_does_not_divide_by_zero()
    {
        var page = new PagedResult<string>([], 100, 1, 0);

        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void Knows_where_it_sits_in_the_sequence()
    {
        var middle = new PagedResult<string>(["a"], 100, 3, 20);

        Assert.True(middle.HasNext);
        Assert.True(middle.HasPrevious);

        var first = new PagedResult<string>(["a"], 100, 1, 20);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);

        var last = new PagedResult<string>(["a"], 100, 5, 20);
        Assert.False(last.HasNext);
        Assert.True(last.HasPrevious);
    }

    [Fact]
    public void Empty_is_a_valid_page_not_a_null()
    {
        var page = PagedResult<string>.Empty(1, 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.False(page.HasNext);
        Assert.False(page.HasPrevious);
    }
}
