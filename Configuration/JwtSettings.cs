using System.Text;

namespace JaliscoProblemsAPI.Configuration;

public sealed record JwtSettings(string Key, string Issuer, string Audience)
{
    public static JwtSettings Load(IConfiguration configuration)
    {
        var environmentKey = configuration["JWT_KEY"];
        var configuredKey = configuration["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(environmentKey) && !string.IsNullOrWhiteSpace(configuredKey)
            && environmentKey != configuredKey)
            throw new InvalidOperationException("JWT_KEY and Jwt:Key must match when both are configured.");

        var key = !string.IsNullOrWhiteSpace(environmentKey) ? environmentKey : configuredKey;
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure a JWT signing key of at least 32 UTF-8 bytes using JWT_KEY or Jwt:Key.");
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required.");
        return new(key, issuer, audience);
    }
}
