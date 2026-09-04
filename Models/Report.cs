
namespace JaliscoProblemsAPI.Models.Report;

public class Report
{
    // the userid the report is associated to (the user that created the report)
    public int UserID { get; set; } = 0;

    public double MarkerLatitude { get; set; } = 0;

    public double MarkerLongitude {get; set;} = 0;

    public string Description {get; set; } = string.Empty;

    public string ReportType {get; set; } = string.Empty;

}