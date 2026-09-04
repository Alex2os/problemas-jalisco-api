// Jwt stands for Json Web Tokens
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

using System.Security.Claims; // we use this for the claims.
// claims are like a statement about a subject that an issuer does, in this case the api is the issuer, so we make a claim in this case about a user.
// in a claim we can find information or attributes about the subject that are helpful in the context of authentication/authorization operations, like a login, for example. 
using JaliscoProblemsAPI.Models.User;
namespace JaliscoProblemsAPI.Services.TokenService;

public class TokenService
{

    IConfiguration _IConfiguration;

    public TokenService(IConfiguration _iconfiguration)
    {
        _IConfiguration = _iconfiguration;
    }

    public string CreateToken(User user)
    {

        // the purpose of using ?? in this case is the null coalescing operator.
        // this operator uses the left value if it's not null. otherwise, it uses the right value
        // we take advantage of this throwing an exception when any of our variables doesnt get found, throwing an exception.

        // we declare all of our Jwt variables we will need in this code.
        var JwtKey = _IConfiguration["Jwt:Key"] ?? throw new Exception("JwtKey no encontrado.");
        var JwtIssuer = _IConfiguration["Jwt:Issuer"] ?? throw new Exception("JwtIssuer no encontrado.");;
        var JwtAudience = _IConfiguration["Jwt:Audience"] ?? throw new Exception("JwtAudience no encontrado.");;


        // we create a new list that contains Claims, so in this case we add some useful information about the user in this case.
        var claims = new List<Claim>
        {
            new Claim("user_id", user.ID.ToString()), // each claim has a key-value structure, like we can see here, but we can only put strings on both.
            new Claim("user_email", user.Email),
            new Claim("username", user.Username),

        };

        // in this case we grab our key, which is the Jwt key we obtain from the appsettings.json and turn it into a proper key for the credential variable,-
        // which is a SigningCredentials class. we do this because we need the key encoded in UTF8 and then obtain the bytes to be used properly by the credentials-
        // variable.
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(JwtKey)
        );

        // now for the credentials, we use the key we previously prepared and using the security algorithm to create a signature for the token.
        // this is important, because later we can verify a token's signature against our secret key to check if it's signed the same, and if it's not it means that-
        // it has been changed or modified, so we use this as a checking method.
        // in this case we use the HmacSha256 hash algorithm.
        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        // with all of the information above, we can now create the token.
        // in this case we specify the issuer, audience, claims, and the credentials, also including a expires field which is when the token will expire.
        // for our application, the token expires in 7 days. 
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7), // in the case of DateTime.UtcNow, this returns a date object of the current time on the computer. when using-
                                                 // AddDays or AddHours we add them to that object, so in this case we specify that from this date on 7 days the token will expire.
            signingCredentials: credentials
        );

        // finally, we return our created Jwt token. in this case the token is sent as a string.
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}