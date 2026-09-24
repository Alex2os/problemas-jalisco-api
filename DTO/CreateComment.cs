using System.ComponentModel.DataAnnotations;

namespace JaliscoProblemsAPI.DTOs.Reports.Comment;

// to create a comment we only need the user_comment itself, as the other data needed, like report_id is gotten from the url, and the user_id is gotten -
// automatically by the system when checking for the autorization.

public class CreateCommentRequestDTO
{

    [Required(ErrorMessage = "Se necesita introducir un comentario")]
    [MaxLength(100, ErrorMessage = "El comentario es muy largo.")]
    public string user_comment { get; set; } = string.Empty;

}