using System.ComponentModel.DataAnnotations;

namespace JaliscoProblemsAPI.DTOs.Locations;

public sealed class ReverseLocationQuery : IValidatableObject
{
    [Required, Range(18.92, 22.75)]
    public double? Latitude { get; set; }

    [Required, Range(-105.68, -101.50)]
    public double? Longitude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude.HasValue && !double.IsFinite(Latitude.Value)
            || Longitude.HasValue && !double.IsFinite(Longitude.Value))
            yield return new ValidationResult("Las coordenadas deben ser números finitos.", [nameof(Latitude), nameof(Longitude)]);
    }
}
