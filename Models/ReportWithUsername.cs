using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed class ReportWithUsername : Report
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "reported";
    [JsonPropertyName("status_updated_at")]
    public DateTime StatusUpdatedAt { get; set; }
    [JsonPropertyName("status_version")]
    public int StatusVersion { get; set; }
    [JsonPropertyName("can_manage")]
    public bool CanManage { get; set; }
    [JsonPropertyName("photos")]
    public List<ReportPhoto> Photos { get; set; } = [];
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("like_count")]
    public long LikeCount { get; set; }

    [JsonPropertyName("comment_count")]
    public long CommentCount { get; set; }

    [JsonPropertyName("saved_by_me")]
    public bool SavedByMe { get; set; }

    [JsonPropertyName("liked_by_me")]
    public bool LikedByMe { get; set; }
}
