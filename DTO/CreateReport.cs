using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.DTOs.Reports;

public sealed class CreateReportRequestDTO
{
    [JsonPropertyName("description")]
    [MaxLength(255, ErrorMessage = "La descripción introducida es muy larga.")]
    public string? Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [Required(ErrorMessage = "Se necesita introducir un tipo de reporte.")]
    [MaxLength(20, ErrorMessage = "El tipo de reporte introducido es muy largo.")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    [Range(18.92, 22.75, ErrorMessage = "La latitud debe estar entre 18.92 y 22.75.")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    [Range(-105.68, -101.50, ErrorMessage = "La longitud debe estar entre -105.68 y -101.50.")]
    public double Longitude { get; set; }
}
