using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed record ReportRecommendation(
    [property: JsonPropertyName("report_id")] int ReportId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("distance_km")] double? DistanceKm);
