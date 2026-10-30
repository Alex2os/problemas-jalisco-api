using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.DTOs.Reports;

namespace JaliscoProblemsAPI.Tests;

public sealed class RecommendedReportsQueryTests
{
    private static bool Valid(RecommendedReportsQuery query) => Validator.TryValidateObject(query, new ValidationContext(query), [], true);

    [Fact]
    public void DefaultsAllowGuestsWithoutLocation()
    {
        var query = new RecommendedReportsQuery();
        Assert.True(Valid(query));
        Assert.Equal(20, query.Limit);
        Assert.Equal(0, query.Offset);
    }

    [Theory]
    [InlineData(20.67, -103.35, true)]
    [InlineData(-90, 180, true)]
    [InlineData(91, 0, false)]
    [InlineData(90.01, 0, false)]
    [InlineData(0, 180.01, false)]
    [InlineData(0, -181, false)]
    [InlineData(double.NaN, 0, false)]
    [InlineData(0, double.PositiveInfinity, false)]
    public void CoordinatesMustBeFiniteAndInRange(double latitude, double longitude, bool valid) =>
        Assert.Equal(valid, Valid(new() { Latitude = latitude, Longitude = longitude }));

    [Fact]
    public void CoordinatesMustBeProvidedTogether()
    {
        Assert.False(Valid(new() { Latitude = 20 }));
        Assert.False(Valid(new() { Longitude = -103 }));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(51, 0, false)]
    [InlineData(20, -1, false)]
    [InlineData(50, 10, true)]
    public void PaginationIsValidated(int limit, int offset, bool valid) =>
        Assert.Equal(valid, Valid(new() { Limit = limit, Offset = offset }));
}
