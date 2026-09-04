// this is a model for the user objects handled inside the backend.

namespace JaliscoProblemsAPI.Models.User;

public class User()
{
    public int ID {get; set; } = 0;
    public string Email {get; set; } = string.Empty;
    public string PasswordHash {get; set; } = string.Empty;
    public string Username {get; set;} = string.Empty;

}