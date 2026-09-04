using Npgsql;
using JaliscoProblemsAPI.Data.DataBaseConnection;
using JaliscoProblemsAPI.Models.Report;

namespace JaliscoProblemsAPI.Data.ReportRepository;


public class ReportRepository
{
    private readonly NpgsqlConnection _DBConnection;

    public ReportRepository(DBConnection db_connection)
    {
        _DBConnection = db_connection.ConnectToDB();
    }

    public async Task CreateReport(Report report)
    {

        try
        {
            string sql_query = "INSERT INTO reports (marker_longitude, marker_latitude, description, report_type, user_id) VALUES (@marker_longitude, @marker_latitude, @description, @report_type, @user_id)";

            var command = new NpgsqlCommand(sql_query, _DBConnection);

            command.Parameters.AddWithValue("@marker_longitude", report.MarkerLongitude);
            command.Parameters.AddWithValue("@marker_latitude", report.MarkerLatitude);
            command.Parameters.AddWithValue("@description", report.Description);
            command.Parameters.AddWithValue("@report_type", report.ReportType);
            command.Parameters.AddWithValue("@user_id", report.UserID);

            var result = await command.ExecuteNonQueryAsync();

        }
        catch (Exception ex)
        {
            throw new Exception("Hubo un error creando el reporte.", ex);
        }
    }

    // this function returns all the reports in the database.
    public async Task<List<Report>> GetReports()
    {
        List<Report> reports_found = new List<Report>();

        try
        {

            string sql_query = "SELECT * FROM reports";

            var command = new NpgsqlCommand(sql_query, _DBConnection);

            var reader = command.ExecuteReader();

            while (reader.Read())
            {

                Report report = new Report();

                // for now we dont use the first column (0) because we dont want to use the reportid. maybe in the future it would be good to include this.
                report.MarkerLongitude = reader.GetDouble(1);
                report.MarkerLatitude = reader.GetDouble(2);
                report.Description = reader.GetString(3);
                report.ReportType = reader.GetString(4);
                report.UserID = reader.GetInt32(6);

                // we add the report to the end of the list.
                reports_found.Add(report);

            }

            return reports_found;


        }
        catch (Exception ex){throw new Exception("Hubo un error obteniendo los reportes.", ex);}
    
    }
}