using System.ComponentModel.DataAnnotations;

namespace JaliscoProblemsAPI.DTOs.Reports;

public sealed class RecommendedReportsQuery : IValidatableObject
{
    [Range(-90.0, 90.0)]
    public double? Latitude { get; set; }

    [Range(-180.0, 180.0)]
    public double? Longitude { get; set; }

    [Range(1, 50)]
    public int Limit { get; set; } = 20;

    [Range(0, int.MaxValue - 50)]
    public int Offset { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude.HasValue != Longitude.HasValue)
            yield return new ValidationResult("Introduce latitud y longitud juntas.", [nameof(Latitude), nameof(Longitude)]);
        if (Latitude.HasValue && !double.IsFinite(Latitude.Value)
            || Longitude.HasValue && !double.IsFinite(Longitude.Value))
            yield return new ValidationResult("Las coordenadas deben ser números finitos.", [nameof(Latitude), nameof(Longitude)]);
    }
}
