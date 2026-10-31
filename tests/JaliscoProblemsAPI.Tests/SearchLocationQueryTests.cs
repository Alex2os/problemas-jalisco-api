using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.DTOs.Locations;

namespace JaliscoProblemsAPI.Tests;

public sealed class SearchLocationQueryTests
{
    private static bool Valid(string? query)
    {
        var request = new SearchLocationQuery { Query = query! };
        return Validator.TryValidateObject(request, new ValidationContext(request), [], true);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ab", false)]
    [InlineData(" a ", false)]
    [InlineData("a  b", true)]
    [InlineData("Av. Patria, Zapopan", true)]
    [InlineData("  Calle\nMorelos  ", true)]
    public void RequiresMeaningfulBoundedQuery(string? query, bool expected) => Assert.Equal(expected, Valid(query));

    [Fact]
    public void CapsRawQueryLengthAt200Characters()
    {
        Assert.True(Valid(new string('x', 200)));
        Assert.False(Valid(new string('x', 201)));
    }
}
