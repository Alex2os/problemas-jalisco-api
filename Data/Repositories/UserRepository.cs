using Npgsql;
using JaliscoProblemsAPI.Data.DataBaseConnection;
using JaliscoProblemsAPI.Models.User;

namespace JaliscoProblemsAPI.Data.UserRepository;

public class UserRepository
{
    // we use a NpgsqlConnection for the connection to the database and to execute the queries properly, instead of previously having a DBConnection class. this is to prevent errors when doing sql queries.
    private readonly NpgsqlConnection _DBConnection;

    public UserRepository(DBConnection db_connection) // the DBConnection object/class is assigned and sent here automatically by the program, 
    // as we load the DBConnection as a singleton in the Program.cs, so when an object is constructed it automatically has this.
    {

        _DBConnection = db_connection.ConnectToDB();  // we connect to the database everytime an object of this class is created.
        // we use the .ConnectToDB() method to obtain the connection. we obtain a new fresh connection everytime to not have any bugs.
    }

    // function to check if an email exists. we do the same for the username, as this two are the ones that need to be checked.
    public async Task<bool> EmailExists(string email)
    {
        try
        {

            // with this query we can select 1 register in the db table "users" if the request email already exists. depending on this, we return true or false in this function.
            string sql_query = @"SELECT EXISTS (
                SELECT 1
                FROM users
                WHERE email = @request_email
                )";

            var command = new NpgsqlCommand(sql_query, _DBConnection);
            command.Parameters.AddWithValue("@request_email", email);

            var result = await command.ExecuteScalarAsync(); // the ExecuteScalarAsync returns the first row of the first column of the result of the query, ignoring all the other results. this is useful in this case for a single result.
                                                             // the function above this comment returns null if it hasn't find anything.

            // in this case we just return the result converted to boolean. the ExecuteScalarAsync returns an object, so we can't just directly send result, so we just convert it to boolean.
            return Convert.ToBoolean(result);

        }
        catch (NpgsqlException ex) // we use a npgsqlexception here, as any problem that could happen would be with the database connection.
        {
            throw new Exception("Hubo un error revisando el email en la base de datos.", ex); // we don't need to type a return after the exception, as the exception itself is like a return. any code below this will not be run.
        }

    }

    public async Task<bool> UsernameExists(string username)
    {

        try
        {

            string sql_query = @"SELECT EXISTS(
            SELECT 1
            FROM users
            WHERE username = @request_username
            )";

            var command = new NpgsqlCommand(sql_query, _DBConnection);
            command.Parameters.AddWithValue("@request_username", username);

            var result = await command.ExecuteScalarAsync();

            return Convert.ToBoolean(result);

        }
        catch (NpgsqlException ex)
        {
            throw new Exception("Hubo un error revisando el nombre de usuario en la base de datos.", ex);
        }

    }

    // function to create a new user. a user is needed to be provided.
    public async Task CreateUser(User user)
    {

        try
        {
            // in the query, we dont have to use @_username for example, because that gives errors when using the AddWithValue() function. so we keep it like it is right now.
            string sql_query = "INSERT INTO users (username, email, passwordhash) VALUES(@username, @email, @passwordhash)";

            var command = new NpgsqlCommand(sql_query, _DBConnection);
            // we replace the @'s with the user attributes or values. we need multiple of the .AddWithValue to add multiple things.
            command.Parameters.AddWithValue("@username", user.Username);
            command.Parameters.AddWithValue("@email", user.Email);
            command.Parameters.AddWithValue("@passwordhash", user.PasswordHash);

            var result = await command.ExecuteNonQueryAsync(); // this executes the query and returns the number of rows affected.

        }
        catch (Exception ex)
        {
            // we can use console.error.writeline to show an error correctly.
            // Console.Error.WriteLine("Hubo un error creando el usuario.", ex);

            throw new Exception("Hubo un error creando el usuario.", ex);

        }
    }

    // with this function, we return a user based on its email, returning an user object.
    public async Task<User> GetUser(string email)
    {
        try
        {
            string sql_query = "SELECT * FROM users WHERE email = @email";
            var command = new NpgsqlCommand(sql_query, _DBConnection);
            command.Parameters.AddWithValue("@email", email);

            // with execute reader we obtain a reader to get the results. we declare a new User variable and put the results there to return it.
            var reader = command.ExecuteReader();

            var user_obtained = new User();

            while (reader.Read())
            {

                // the numbers in the (1), (2) and (3) are the columns in this case. the reader returns each row.
                user_obtained.ID = reader.GetInt32(0);
                user_obtained.Username = reader.GetString(1);
                user_obtained.Email = reader.GetString(2);
                user_obtained.PasswordHash = reader.GetString(3);

            }

            return user_obtained;

        }
        catch (Exception ex) { throw new Exception("Hubo un error obteniendo el usuario.", ex); }
    }



}