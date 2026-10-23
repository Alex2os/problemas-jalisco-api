using Npgsql;

namespace JaliscoProblemsAPI.Data;

public static class DatabaseConnection
{
    public static string GetConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        var builder = !string.IsNullOrWhiteSpace(configured)
            ? new NpgsqlConnectionStringBuilder(configured)
            : new NpgsqlConnectionStringBuilder
            {
                Host = configuration["DB_HOST"],
                Username = configuration["DB_USERNAME"],
                Password = configuration["DB_PASSWORD"],
                Database = configuration["DB_DATABASE"],
                Port = ReadPort(configuration["DB_PORT"])
            };
        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Username)
            || string.IsNullOrWhiteSpace(builder.Database))
            throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection or DB_HOST, DB_USERNAME, DB_DATABASE and DB_PORT.");
        // Keep SQL values out of provider error messages and logs.
        builder.IncludeErrorDetail = false;
        builder.LogParameters = false;
        return builder.ConnectionString;
    }

    private static int ReadPort(string? value) => int.TryParse(value, out var port) && port is > 0 and <= 65535
        ? port : throw new InvalidOperationException("DB_PORT must be a valid port number.");
}
