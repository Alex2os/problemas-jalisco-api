using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using JaliscoProblemsAPI.Controllers;
using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.DTOs.Reports;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JaliscoProblemsAPI.Tests;

public sealed class CategoryContractTests
{
    private static bool Valid(object request, out List<ValidationResult> results)
    {
        results = [];
        return Validator.TryValidateObject(request, new ValidationContext(request), results, true);
    }

    [Fact]
    public void ActiveCategoriesAreSevenAndOrderedCorrectly()
    {
        var active = ReportCategories.ActiveCategories;
        Assert.Equal(7, active.Count);

        var expectedIds = new[]
        {
            "baches",
            "alumbrado",
            "agua",
            "basura",
            "seguridad",
            "arboles",
            "otro"
        };

        Assert.Equal(expectedIds, active.Select(c => c.Id));

        // Verificar que cada categoría activa tenga id, name y description válidos y no vacíos
        foreach (var category in active)
        {
            Assert.False(string.IsNullOrWhiteSpace(category.Id));
            Assert.False(string.IsNullOrWhiteSpace(category.Name));
            Assert.False(string.IsNullOrWhiteSpace(category.Description));
            Assert.True(category.Id.Length <= 20, $"ID '{category.Id}' exceeds 20 characters.");
            Assert.True(ReportCategories.IsActive(category.Id));
            Assert.True(ReportCategories.IsRecognized(category.Id));
            Assert.False(ReportCategories.IsLegacy(category.Id));
        }
    }

    [Fact]
    public void LegacyCategoriesAreRecognizedSeparatelyAndNotTreatedAsActive()
    {
        var legacyIds = new[]
        {
            "accidente",
            "bloqueo",
            "corte-de-servicios",
            "transporte-publico",
            "robo-asalto",
            "otros"
        };

        foreach (var id in legacyIds)
        {
            Assert.True(ReportCategories.IsLegacy(id), $"Expected '{id}' to be recognized as legacy.");
            Assert.False(ReportCategories.IsActive(id), $"Legacy ID '{id}' must not be in active categories.");
            Assert.True(ReportCategories.IsRecognized(id));
            Assert.DoesNotContain(id, ReportCategories.ActiveCategories.Select(c => c.Id));
        }
    }

    [Theory]
    [InlineData("baches", true)]
    [InlineData("alumbrado", true)]
    [InlineData("agua", true)]
    [InlineData("basura", true)]
    [InlineData("seguridad", true)]
    [InlineData("arboles", true)]
    [InlineData("otro", true)]
    [InlineData("otros", true)] // Alias de compatibilidad permitido para creación
    [InlineData("accidente", false)]
    [InlineData("bloqueo", false)]
    [InlineData("corte-de-servicios", false)]
    [InlineData("transporte-publico", false)]
    [InlineData("robo-asalto", false)]
    [InlineData("categoria_inventada", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ValidationAttributeAcceptsOnlyActiveCategoriesAndOtrosAlias(string category, bool expectedValid)
    {
        var dto = new CreateReportRequestDTO
        {
            Title = "Reporte de prueba",
            Type = category,
            Latitude = 20.6597,
            Longitude = -103.3496,
            Description = "Prueba de validación"
        };

        var isValid = Valid(dto, out var results);
        Assert.Equal(expectedValid, isValid);

        if (!expectedValid)
        {
            Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateReportRequestDTO.Type)));
            var errors = results.Where(r => r.MemberNames.Contains(nameof(CreateReportRequestDTO.Type))).Select(r => r.ErrorMessage);
            if (string.IsNullOrWhiteSpace(category))
            {
                Assert.Contains(errors, e => e != null && (e.Contains("Se necesita introducir un tipo de reporte") || e.Contains("La categoría especificada no es válida")));
            }
            else
            {
                Assert.Contains(errors, e => e != null && e.Contains("La categoría especificada no es válida"));
            }
        }
    }

    [Fact]
    public void NormalizationConvertsOtrosToOtroAndPreservesActiveCategories()
    {
        Assert.Equal("otro", ReportCategories.Normalize("otros"));
        Assert.Equal("otro", ReportCategories.Normalize("otro"));
        Assert.Equal("baches", ReportCategories.Normalize("baches"));
        Assert.Equal("alumbrado", ReportCategories.Normalize("alumbrado"));
        Assert.Equal("agua", ReportCategories.Normalize("agua"));
        Assert.Equal("basura", ReportCategories.Normalize("basura"));
        Assert.Equal("seguridad", ReportCategories.Normalize("seguridad"));
        Assert.Equal("arboles", ReportCategories.Normalize("arboles"));
    }

    [Fact]
    public void CategoriesEndpointReturnsOkWithActiveCategoriesContract()
    {
        // ReportsController constructor requires (ReportRepository, GeocodingService)
        // Since GetCategories does not use database or geocoding, null! is acceptable
        var controller = new ReportsController(null!, null!);
        var result = controller.GetCategories();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var json = JsonSerializer.SerializeToElement(okResult.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Las categorías se obtuvieron con éxito.", json.GetProperty("message").GetString());

        var categoriesElement = json.GetProperty("categories");
        Assert.Equal(JsonValueKind.Array, categoriesElement.ValueKind);
        Assert.Equal(7, categoriesElement.GetArrayLength());

        var first = categoriesElement[0];
        Assert.Equal("baches", first.GetProperty("id").GetString());
        Assert.Equal("Baches y vialidad", first.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("description").GetString()));
    }
}
