using JaliscoProblemsAPI.Models;
using Npgsql;

namespace JaliscoProblemsAPI.Data.Repositories;

public enum CreateUserResult { Created, EmailExists, UsernameExists }

public sealed class UserRepository(NpgsqlDataSource dataSource)
{
    public async Task<CreateUserResult> CreateUserAsync(User user, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO users (username, email, passwordhash) VALUES ($1, $2, $3)");
        command.Parameters.AddWithValue(user.Username);
        command.Parameters.AddWithValue(user.Email);
        command.Parameters.AddWithValue(user.PasswordHash);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return CreateUserResult.Created;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName is "users_email_key" or "users_username_key")
        {
            return exception.ConstraintName == "users_email_key" ? CreateUserResult.EmailExists : CreateUserResult.UsernameExists;
        }
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, username, email, passwordhash FROM users WHERE email = $1");
        command.Parameters.AddWithValue(email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new User
        {
            Id = reader.GetInt32(0), Username = reader.GetString(1),
            Email = reader.GetString(2), PasswordHash = reader.GetString(3)
        };
    }
}
