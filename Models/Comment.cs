using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public class Comment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("report_id")]
    public int ReportId { get; set; }

    [JsonPropertyName("user_comment")]
    public string UserComment { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
