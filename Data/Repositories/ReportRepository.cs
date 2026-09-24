using Npgsql;
using JaliscoProblemsAPI.Data.DataBaseConnection;
using JaliscoProblemsAPI.Models.ReportWithUsername;
using JaliscoProblemsAPI.Models.Report;
using System.Net.Http;
using System.Text.Json;


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
            string sql_query = "INSERT INTO reports (longitude, latitude, ubication, description, type, user_id) VALUES (@longitude, @latitude, @ubication, @description, @type, @user_id)";

            var command = new NpgsqlCommand(sql_query, _DBConnection);

            // we obtain the ubication for the report
            string? ubication = await ObtainReportUbication(report.latitude, report.longitude);

            command.Parameters.AddWithValue("@longitude", report.longitude);
            command.Parameters.AddWithValue("@latitude", report.latitude);
            command.Parameters.AddWithValue("@ubication", ubication);
            command.Parameters.AddWithValue("@description", report.description);
            command.Parameters.AddWithValue("@type", report.type);
            command.Parameters.AddWithValue("@user_id", report.user_id);

            var result = await command.ExecuteNonQueryAsync();

        }
        catch (Exception ex)
        {
            throw new Exception("Hubo un error creando el reporte.", ex);
        }
    }

    public async Task<List<ReportWithUsername>> GetFilteredReports(string query)
    {
        List<ReportWithUsername> reports_found = new List<ReportWithUsername>();

        try
        {

            // in this case it's the same query as the getallreports, but now we include the ilike part, that part being the query that is sent to this function.
            // we use the %% to specify that we want to find the registers or reports that contain that query anywhere.
            string sql_query = @"SELECT 
            r.id, r.longitude, r.latitude, r.ubication, r.description, r.type, r.created_at, r.user_id, u.username 
            FROM reports r JOIN users u ON r.user_id = u.id WHERE r.ubication ILIKE @query ORDER BY r.id DESC";

            var command = new NpgsqlCommand(sql_query, _DBConnection);
            command.Parameters.AddWithValue("@query", $"%{query}%"); // we have to do this in order for the ilike query to go right, as there's some weird -
            // behavior with this specifically. we specify the the string, then the % operators for the query, and then the query itself. with this,-
            // this works properly with the ilike.

            var reader = command.ExecuteReader();

            while (reader.Read())
            {

                ReportWithUsername report = new ReportWithUsername
                {
                    id = reader.GetInt32(0),
                    longitude = reader.GetDouble(1),
                    latitude = reader.GetDouble(2),
                    ubication = reader.GetString(3),
                    description = reader.GetString(4),
                    type = reader.GetString(5),
                    created_at = reader.GetDateTime(6),
                    user_id = reader.GetInt32(7),
                    username = reader.GetString(8)
                };

                reports_found.Add(report);
            }

            return reports_found;

        }
        catch (Exception ex)
        {
            throw new Exception("Hubo un error obteniendo los reportes filtrados.", ex);
        }

    }

    // this function returns all the reports in the database.
    public async Task<List<ReportWithUsername>> GetAllReports()
    {
        List<ReportWithUsername> reports_found = new List<ReportWithUsername>();

        try
        {

            // we obtain both the report info and the username using this sql join query.
            // we obtain the reports by desc based on the reports id column, so we obtain first the latest reports done. this is so for example in the reports-
            // page, we obtain the latest reports first instead of the first ones made.
            string sql_query = @"SELECT 
            r.id, r.longitude, r.latitude, r.ubication, r.description, r.type, r.created_at, r.user_id, u.username 
            FROM reports r JOIN users u ON r.user_id = u.id ORDER BY r.id DESC";

            var command = new NpgsqlCommand(sql_query, _DBConnection);

            var reader = command.ExecuteReader();

            while (reader.Read())
            {

                // we create a new report using the model reportwithusername, so we can include who made the report.
                ReportWithUsername report = new ReportWithUsername
                {
                    id = reader.GetInt32(0),
                    longitude = reader.GetDouble(1),
                    latitude = reader.GetDouble(2),
                    ubication = reader.GetString(3),
                    description = reader.GetString(4),
                    type = reader.GetString(5),
                    created_at = reader.GetDateTime(6),
                    user_id = reader.GetInt32(7),
                    username = reader.GetString(8)
                };

                // we add the report to the end of the list.
                reports_found.Add(report);
            }

            return reports_found;

        }
        catch (Exception ex) { throw new Exception("Hubo un error obteniendo todos los reportes.", ex); }

    }

    // function to obtain the actual ubication of the report based on latitude and longitude
    public async Task<string?> ObtainReportUbication(double latitude, double longitude)
    {
        // as we obtain the ubication through an api, we need to make http calls to it. we first declare a client and specify the useragent below.
        HttpClient client = new HttpClient();

        // user agent is the api/application itself, the one that is making the call
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProblemasJaliscoAPI/1.0");

        // we make our string for the url of the api we are calling. we specify the latitude and longitude inside the url.
        string url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={latitude}&lon={longitude}&addressdetails=1";

        // we send the request or call here. with GetAsync we specify that it's an asynchronous operation (async).
        HttpResponseMessage response = await client.GetAsync(url);

        // we then check the success status code. if the response was not correct, we return null.
        if (!response.IsSuccessStatusCode) return null;

        // if everything went correct we can obtain the json as a string first
        string json_string = await response.Content.ReadAsStringAsync();

        // we then can obtain the json document itself
        JsonDocument json = JsonDocument.Parse(json_string);

        // finally we can return the json. in this case we specify that we are returning the "display_name" property of the json itself.
        // root element helps us to obtain the rootelement of the json, then we can obtain the property and turn it into string.
        return json.RootElement.GetProperty("display_name").ToString();
    }
}