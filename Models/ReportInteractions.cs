using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed class ReportInteractions
{
    [JsonPropertyName("report_id")]
    public int ReportId { get; set; }
    [JsonPropertyName("like_count")]
    public long LikeCount { get; set; }
    [JsonPropertyName("comment_count")]
    public long CommentCount { get; set; }
    [JsonPropertyName("liked_by_me")]
    public bool LikedByMe { get; set; }
}
