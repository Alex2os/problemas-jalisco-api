namespace JaliscoProblemsAPI.Services;

public sealed class PasswordService
{
    // Unknown users still perform a BCrypt verification to avoid the fast-path email oracle.
    private readonly string _dummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool CheckPassword(string password, string? hash)
    {
        var matches = BCrypt.Net.BCrypt.Verify(password, hash ?? _dummyHash);
        return hash is not null && matches;
    }
}
