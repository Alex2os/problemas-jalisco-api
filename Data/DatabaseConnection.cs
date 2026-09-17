using Npgsql;
namespace JaliscoProblemsAPI.Data.DataBaseConnection;

public class DBConnection
{
    public DBConnection() { }


    private NpgsqlConnection CreateConnection()
    {

        try
        {

            var connection_string = new NpgsqlConnectionStringBuilder
            {
                Host = Environment.GetEnvironmentVariable("DB_HOST"),
                Username = Environment.GetEnvironmentVariable("DB_USERNAME"),
                Password = Environment.GetEnvironmentVariable("DB_PASSWORD"),
                Database = Environment.GetEnvironmentVariable("DB_DATABASE"),
                Port = int.Parse(Environment.GetEnvironmentVariable("DB_PORT"))
            };

            NpgsqlConnection Connection = new NpgsqlConnection(connection_string.ConnectionString);

            Connection.Open();
            Console.WriteLine("Connection was successful.");

            return Connection;
        }

        catch (Exception ex) { throw new Exception("Error intentando crear la conexión con la base de datos. Intente más tarde", ex); }
    }

    // everytime we create a new connection, an instance of said connection is sent, instead of the same connection. so everytime a connection is needed a new one is created,-
    // instead of previously having a public variable in this class which did not change.
    // we fixed this because sometimes having the same connection caused trouble when trying to run more sqls querys on the same connection.
    public NpgsqlConnection ConnectToDB() { return CreateConnection(); }
}
