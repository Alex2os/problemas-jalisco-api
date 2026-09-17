
namespace JaliscoProblemsAPI.Models.ReportWithUsername;

// we need a model to assign the username too (to display the user associated to the report), so it's basically the same as the Report.cs model but it has a-
// username field. we make it separately so there's so there's the default db model (Report.cs) and this one, that contains the username.
public class ReportWithUsername
{
    public int id { get; set; } = 0;
    public double longitude { get; set; } = 0;
    public double latitude { get; set; } = 0;
    public string ubication { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
    // we create a datetime variable to hold the date and the time of when the report was created.
    // we can also use DateOnly for only a date without time.
    public DateTime created_at { get; set; } = new DateTime();
    // the userid the report is associated to (the user that created the report)
    public int user_id { get; set; } = 0;

    // username associated with the report.
    public string username { get; set; } = string.Empty;
}