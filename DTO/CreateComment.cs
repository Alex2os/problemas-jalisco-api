using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JaliscoProblemsAPI.DTOs.Reports.Comment;

public sealed class CreateCommentRequestDTO
{
    [JsonPropertyName("user_comment")]
    [Required(ErrorMessage = "Se necesita introducir un comentario.")]
    [MaxLength(100, ErrorMessage = "El comentario es muy largo.")]
    public string UserComment { get; set; } = string.Empty;
}
