using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.DTOs.Locations;

namespace JaliscoProblemsAPI.Tests;

public sealed class ReverseLocationQueryTests
{
    private static bool Valid(ReverseLocationQuery query) => Validator.TryValidateObject(query, new ValidationContext(query), [], true);

    [Fact]
    public void BothCoordinatesAreRequired()
    {
        Assert.False(Valid(new()));
        Assert.False(Valid(new() { Latitude = 20 }));
        Assert.False(Valid(new() { Longitude = -103 }));
    }

    [Theory]
    [InlineData(20.67, -103.35, true)]
    [InlineData(18.92, -105.68, true)]
    [InlineData(22.75, -101.50, true)]
    [InlineData(18.919, -103, false)]
    [InlineData(22.751, -103, false)]
    [InlineData(20, -105.681, false)]
    [InlineData(20, -101.499, false)]
    [InlineData(double.NaN, -103, false)]
    [InlineData(20, double.PositiveInfinity, false)]
    public void CoordinatesMatchReportBoundsAndRejectNonFiniteValues(double latitude, double longitude, bool valid) =>
        Assert.Equal(valid, Valid(new() { Latitude = latitude, Longitude = longitude }));
}
