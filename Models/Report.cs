using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public class Report
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("ubication")]
    public string Ubication { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }
}
