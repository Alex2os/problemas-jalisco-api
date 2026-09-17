
namespace JaliscoProblemsAPI.Models.Report;

// represents the report in the database as it is.
public class Report
{
    public int id { get; set; } = 0;
    public double marker_longitude { get; set; } = 0;
    public double marker_latitude { get; set; } = 0;
    public string description { get; set; } = string.Empty;
    public string report_type { get; set; } = string.Empty;
    // we create a datetime variable to hold the date and the time of when the report was created.
    // we can also use DateOnly for only a date without time.
    public DateTime created_at {get; set; } = new DateTime();
    // the userid the report is associated to (the user that created the report)
    public int user_id { get; set; } = 0;
}