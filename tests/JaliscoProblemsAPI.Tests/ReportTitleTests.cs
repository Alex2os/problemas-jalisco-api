using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using JaliscoProblemsAPI.DTOs.Reports;
using JaliscoProblemsAPI.Models;

namespace JaliscoProblemsAPI.Tests;

public sealed class ReportTitleTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t", false)]
    [InlineData("Bache frente a la escuela", true)]
    [InlineData("  Bache frente a la escuela  ", true)]
    [InlineData("Primera\nSegunda", false)]
    [InlineData("Título\n", false)]
    [InlineData("Primera\rSegunda", false)]
    public void CreationRequiresNonblankSingleLineTitle(string? title, bool expected)
    {
        var request = new CreateReportRequestDTO { Type = "baches", Latitude = 20.6597, Longitude = -103.3496, Title = title! };
        Assert.Equal(expected, Valid(request));
    }

    private static bool Valid(object request) => Validator.TryValidateObject(request, new ValidationContext(request), [], true);

    [Fact]
    public void Enforces80CharacterTitleBoundary()
    {
        var request = new CreateReportRequestDTO { Type = "agua", Latitude = 20.6597, Longitude = -103.3496, Title = new string('x', 80) };
        Assert.True(Valid(request));
        request.Title += "x";
        Assert.False(Valid(request));
        request.Title = string.Concat(Enumerable.Repeat("😀", 41));
        Assert.False(Valid(request));
    }

    [Fact]
    public void SerializesStoredTitleWithoutDerivingItFromDescription()
    {
        var report = new ReportWithUsername { Title = "Bache frente a la escuela", Description = "Lleva semanas sin reparar." };
        var json = JsonSerializer.SerializeToElement(report);
        Assert.Equal(report.Title, json.GetProperty("title").GetString());
        Assert.Equal(report.Description, json.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(new ReportWithUsername()).GetProperty("title").ValueKind);
    }
}
