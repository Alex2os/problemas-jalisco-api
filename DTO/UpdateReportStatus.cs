using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.DTOs.Reports;

public sealed class UpdateReportStatusRequestDTO
{
    [Required, RegularExpression("^(reported|still_present|resolved)$")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [Required, Range(0, int.MaxValue)]
    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; set; }
}
