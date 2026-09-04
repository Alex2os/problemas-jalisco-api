using System;

namespace JaliscoProblemsAPI.Services.PasswordService;

public class PasswordService()
{

    // with this function we send a password to be hashed and we return the result.
    public string HashPassword(string password)
    {
        // we hash the password and return it here at the same time.
        return BCrypt.Net.BCrypt.HashPassword(password); // we use BCrypt to hash the password properly, in this case. BCrypt is a dotnet package.
    }

    // a function to check the hashed password against a password provided from the user. primarily used when user logins.
    public bool CheckHashedPassword(string password, string hash)
    {
        // we can use the .Verify function from BCrypt to check a password and a password hash.
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

}


