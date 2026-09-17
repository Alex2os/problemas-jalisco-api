using Npgsql;
using JaliscoProblemsAPI.Data.DataBaseConnection;
using JaliscoProblemsAPI.Models.ReportWithUsername;
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

            command.Parameters.AddWithValue("@marker_longitude", report.marker_longitude);
            command.Parameters.AddWithValue("@marker_latitude", report.marker_latitude);
            command.Parameters.AddWithValue("@description", report.description);
            command.Parameters.AddWithValue("@report_type", report.report_type);
            command.Parameters.AddWithValue("@user_id", report.user_id);

            var result = await command.ExecuteNonQueryAsync();

        }
        catch (Exception ex)
        {
            throw new Exception("Hubo un error creando el reporte.", ex);
        }
    }

    // this function returns all the reports in the database.
    public async Task<List<ReportWithUsername>> GetReports()
    {
        List<ReportWithUsername> reports_found = new List<ReportWithUsername>();

        try
        {

            // we obtain both the report info and the username using this sql join query.
            string sql_query = @"SELECT 
            r.id, r.marker_longitude, r.marker_latitude, r.description, r.report_type, r.created_at, r.user_id, u.username 
            FROM reports r JOIN users u ON r.user_id = u.id";

            var command = new NpgsqlCommand(sql_query, _DBConnection);

            var reader = command.ExecuteReader();

            while (reader.Read())
            {

                ReportWithUsername report = new ReportWithUsername();

                // for now we dont use the first column (0) because we dont want to use the reportid. maybe in the future it would be good to include this.
                report.id = reader.GetInt32(0);
                report.marker_longitude = reader.GetDouble(1);
                report.marker_latitude = reader.GetDouble(2);
                report.description = reader.GetString(3);
                report.report_type = reader.GetString(4);
                report.created_at = reader.GetDateTime(5);
                report.user_id = reader.GetInt32(6);
                report.username = reader.GetString(7);

                // we add the report to the end of the list.
                reports_found.Add(report);

            }

            return reports_found;


        }
        catch (Exception ex){throw new Exception("Hubo un error obteniendo los reportes.", ex);}
    
    }
}