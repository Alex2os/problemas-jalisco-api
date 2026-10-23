using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.Models;

public sealed class CommentWithUsername : Comment
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }
}
