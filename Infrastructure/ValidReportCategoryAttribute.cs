using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.Models;

namespace JaliscoProblemsAPI.Infrastructure;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidReportCategoryAttribute : ValidationAttribute
{
    public ValidReportCategoryAttribute()
        : base("La categoría especificada no es válida. Las categorías válidas son: " +
               string.Join(", ", ReportCategories.ActiveCategories.Select(c => c.Id)) + ".")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string categoryId || string.IsNullOrWhiteSpace(categoryId))
            return false;

        return ReportCategories.IsAllowedForCreation(categoryId);
    }
}

