using System.Text.Json.Serialization;
namespace JaliscoProblemsAPI.Models;

public sealed record ReportPhoto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("display_order")] int DisplayOrder,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);
public sealed record ReportStatusEntry(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("previous_status")] string? PreviousStatus,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("actor_id")] int? ActorId,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("changed_at")] DateTime ChangedAt,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] string Source);
