using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using JaliscoProblemsAPI.Configuration;
using JaliscoProblemsAPI.Data;
using JaliscoProblemsAPI.DTOs.Auth;
using JaliscoProblemsAPI.DTOs.Reports;
using JaliscoProblemsAPI.DTOs.Reports.Comment;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace JaliscoProblemsAPI.Tests;

public sealed class ContractTests
{
    private const string TestKey = "synthetic-test-signing-key-with-at-least-32-bytes";
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static bool Valid(object request) => Validator.TryValidateObject(request, new ValidationContext(request), [], true);
    private static RegisterUserRequestDTO Registration(string password = "password123", string username = "usuario") => new() { Email = "test@example.invalid", Username = username, Password = password };

    [Fact]
    public void SigningAndValidationUseTheSameLegacyEnvironmentSetting()
    {
        var settings = JwtSettings.Load(Config(new() { ["JWT_KEY"] = TestKey, ["Jwt:Issuer"] = "test-issuer", ["Jwt:Audience"] = "test-mobile" }));
        var token = new TokenService(settings).CreateToken(new User { Id = 42, Username = "usuario", Email = "test@example.invalid" });
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = settings.Issuer, ValidAudience = settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        }, out var validated);
        Assert.Equal("42", principal.FindFirst("user_id")?.Value);
        Assert.Equal("usuario", principal.FindFirst("username")?.Value);
        Assert.InRange(validated.ValidTo - DateTime.UtcNow, TimeSpan.FromDays(6.99), TimeSpan.FromDays(7.01));
    }

    [Fact]
    public void MisconfiguredKeysFailWithoutIncludingTheirValues()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => JwtSettings.Load(Config(new() { ["JWT_KEY"] = TestKey, ["Jwt:Key"] = "different-secret" })));
        Assert.DoesNotContain(TestKey, exception.Message);
        Assert.DoesNotContain("different-secret", exception.Message);
        Assert.Throws<InvalidOperationException>(() => JwtSettings.Load(Config(new() { ["JWT_KEY"] = "short" })));
    }

    [Fact]
    public void JwtColonConfigurationRemainsSupported()
    {
        Assert.Equal(TestKey, JwtSettings.Load(Config(new() { ["Jwt:Key"] = TestKey, ["Jwt:Issuer"] = "issuer", ["Jwt:Audience"] = "audience" })).Key);
    }

    [Fact]
    public void RegistrationRespectsActualUsernameColumnAndUtf8BcryptLimit()
    {
        Assert.True(Valid(Registration(username: new string('x', 20))));
        Assert.False(Valid(Registration(username: new string('x', 21))));
        Assert.True(Valid(Registration(new string('x', 72))));
        Assert.False(Valid(Registration(new string('x', 73))));
        Assert.True(Valid(Registration(new string('é', 36))));
        Assert.False(Valid(Registration(new string('é', 37))));
        Assert.False(Valid(Registration("short")));
    }

    [Fact]
    public void ExistingLoginPasswordLengthContractRemainsUnchanged()
    {
        Assert.True(Valid(new LoginUserRequestDTO { Email = "test@example.invalid", Password = new string('x', 100) }));
    }

    [Theory]
    [InlineData(18.92, -105.68, true)]
    [InlineData(22.75, -101.50, true)]
    [InlineData(18.91, -103, false)]
    [InlineData(20.6, -101.49, false)]
    [InlineData(double.NaN, -103, false)]
    public void ReportCoordinatesRetainJaliscoBounds(double latitude, double longitude, bool valid)
    {
        Assert.Equal(valid, Valid(new CreateReportRequestDTO { Type = "otros", Latitude = latitude, Longitude = longitude, Description = null }));
    }

    [Fact]
    public void EmptyOrOversizedCommentsAreRejected()
    {
        Assert.False(Valid(new CreateCommentRequestDTO { UserComment = "   " }));
        Assert.False(Valid(new CreateCommentRequestDTO { UserComment = new string('x', 101) }));
        Assert.True(Valid(new CreateCommentRequestDTO { UserComment = new string('x', 100) }));
    }

    [Fact]
    public void ResponseModelsKeepSnakeCaseAndNeverSerializePasswordHash()
    {
        var report = JsonSerializer.SerializeToElement(new ReportWithUsername { UserId = 7, Username = "usuario" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(report.TryGetProperty("user_id", out _));
        Assert.True(report.TryGetProperty("like_count", out _));
        Assert.True(report.TryGetProperty("comment_count", out _));
        Assert.True(report.TryGetProperty("liked_by_me", out _));
        var comment = JsonSerializer.SerializeToElement(new CommentWithUsername { CanDelete = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(comment.GetProperty("can_delete").GetBoolean());
        Assert.True(report.TryGetProperty("created_at", out _));
        Assert.True(report.TryGetProperty("ubication", out _));
        Assert.False(report.TryGetProperty("userId", out _));
        var user = JsonSerializer.Serialize(new User { PasswordHash = "not-for-the-client" });
        Assert.DoesNotContain("not-for-the-client", user);
    }

    [Fact]
    public void ProblemsPreserveExpoMessageAndTraceId()
    {
        var json = JsonSerializer.SerializeToElement(ApiErrors.Create(404, "El reporte no existe.", "trace-test"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Equal("El reporte no existe.", json.GetProperty("message").GetString());
        Assert.Equal("trace-test", json.GetProperty("traceId").GetString());
    }

    [Fact]
    public void ConnectionConfigurationRejectsMissingAndInvalidPorts()
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.GetConnectionString(Config(new())));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.GetConnectionString(Config(new() { ["DB_PORT"] = "65536" })));
    }

    [Fact]
    public void BCryptHashesRemainCompatibleAndUnknownUsersFailVerification()
    {
        var passwords = new PasswordService();
        var hash = BCrypt.Net.BCrypt.HashPassword("existing-password");
        Assert.True(passwords.CheckPassword("existing-password", hash));
        Assert.False(passwords.CheckPassword("wrong-password", hash));
        Assert.False(passwords.CheckPassword("existing-password", null));
    }
}
