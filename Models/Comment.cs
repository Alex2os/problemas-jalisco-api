
namespace JaliscoProblemsAPI.Models.Report.Comment;

public class Comment
{
    public int id { get; set; } = 0;
    public int user_id { get; set; } = 0;
    public int report_id { get; set; } = 0;
    public string user_comment { get; set; } = string.Empty;
    public DateTime created_at { get; set; } = new DateTime();

}