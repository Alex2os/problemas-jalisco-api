// this is a model for the user objects handled inside the backend.

namespace JaliscoProblemsAPI.Models.User;

public class User()
{
    public int id {get; set; } = 0;
    public string email {get; set; } = string.Empty;
    public string passwordhash {get; set; } = string.Empty;
    public string username {get; set;} = string.Empty;

}