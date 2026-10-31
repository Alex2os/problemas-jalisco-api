using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace JaliscoProblemsAPI.DTOs.Locations;

public sealed class SearchLocationQuery : IValidatableObject
{
    [Required(ErrorMessage = "Escribe una dirección o lugar para buscar.")]
    [MaxLength(200, ErrorMessage = "La búsqueda debe tener hasta 200 caracteres.")]
    public string Query { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var normalized = Regex.Replace(Query ?? string.Empty, @"\s+", " ").Trim();
        if (normalized.Length < 3)
            yield return new ValidationResult("Escribe al menos 3 caracteres para buscar una ubicación.", [nameof(Query)]);
    }
}
