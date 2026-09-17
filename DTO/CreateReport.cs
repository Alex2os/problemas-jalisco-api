using System.ComponentModel.DataAnnotations;

namespace JaliscoProblemsAPI.DTOs.Reports;

public class CreateReportRequestDTO
{
    // the description is not required in this case as reports can be created without a description. all other attributes must have information in them
    [MaxLength(255, ErrorMessage = "La descripcion introducida es muy larga.")]
    public string description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Se necesita introducir un tipo de reporte.")]
    [MaxLength(20, ErrorMessage = "El tipo de reporte introducido es muy largo.")]
    public string type { get; set; } = string.Empty;

    // we have some range needed for the latitude and the longitude, in order for the report to be correct. we use the Range() to get that interval or range correctly.
    // the interval or range is got from the frontend, in which we limit the reports to the Jalisco state in Mexico.
    [Required(ErrorMessage = "Se necesita introducir una latitud.")]
    [Range(18.92, 22.75, ErrorMessage = "La latitud debe de estar entre 18.92 y 22.75.")]
    public double latitude { get; set; } = 0;

    [Required(ErrorMessage = "Se necesita introducir una longitud.")]
    [Range(-105.68, -101.50, ErrorMessage = "La longitud debe de estar -105.68 y -101.50.")]
    public double longitude { get; set; } = 0;

}