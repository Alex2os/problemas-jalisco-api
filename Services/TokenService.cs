using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JaliscoProblemsAPI.Configuration;
using JaliscoProblemsAPI.Models;
using Microsoft.IdentityModel.Tokens;

namespace JaliscoProblemsAPI.Services;

public sealed class TokenService(JwtSettings settings)
{
    public string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim("user_id", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("user_email", user.Email),
            new Claim("username", user.Username)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: DateTime.UtcNow.AddDays(7), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
