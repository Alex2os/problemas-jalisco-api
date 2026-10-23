using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using JaliscoProblemsAPI.Configuration;
using JaliscoProblemsAPI.Data;
using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var jwt = JwtSettings.Load(builder.Configuration);
var connectionString = DatabaseConnection.GetConnectionString(builder.Configuration);

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<ReportRepository>();
builder.Services.AddSingleton<IReportPhotoStorage, ReportPhotoStorage>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<GeocodingService>();

var geocodingUrl = builder.Configuration["Geocoding:BaseUrl"] ?? "https://nominatim.openstreetmap.org/";
if (!Uri.TryCreate(geocodingUrl, UriKind.Absolute, out var geocodingBase)
    || geocodingBase.Scheme != Uri.UriSchemeHttps && !(builder.Environment.IsDevelopment() && geocodingBase.IsLoopback && geocodingBase.Scheme == Uri.UriSchemeHttp))
    throw new InvalidOperationException("Geocoding:BaseUrl must be an absolute HTTPS URL (local HTTP is permitted only in Development).");
if (!geocodingBase.AbsoluteUri.EndsWith('/')) throw new InvalidOperationException("Geocoding:BaseUrl must end with '/'.");
builder.Services.AddHttpClient("Geocoding", client =>
{
    client.BaseAddress = geocodingBase;
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(builder.Configuration["Geocoding:UserAgent"] ?? "ProblemasJaliscoAPI/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Logging.AddFilter("System.Net.Http.HttpClient.Geocoding", LogLevel.Warning);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest, Title = "La solicitud no es válida."
        };
        problem.Extensions["message"] = string.Join(" ", problem.Errors.Values.SelectMany(errors => errors));
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);
    };
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (!int.TryParse(context.Principal?.FindFirst("user_id")?.Value, out var userId) || userId <= 0)
                context.Fail("Invalid user identity.");
            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ApiErrors.WriteAsync(context.HttpContext, 401, "Inicia sesión para continuar.", context.HttpContext.RequestAborted);
        },
        OnForbidden = context => ApiErrors.WriteAsync(context.HttpContext, 403, "No tienes permiso para realizar esta acción.", context.HttpContext.RequestAborted)
    };
});
builder.Services.AddAuthorization();

var authLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);
var writeLimit = builder.Configuration.GetValue("RateLimiting:WritePermitLimit", 30);
if (authLimit <= 0 || writeLimit <= 0) throw new InvalidOperationException("Rate limit permit counts must be positive.");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await ApiErrors.WriteAsync(context.HttpContext, 429, "Demasiadas solicitudes. Intenta más tarde.", cancellationToken);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("user_id")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = writeLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "PUT", "DELETE").AllowAnyHeader();
}));
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Problemas Jalisco API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.OperationFilter<SwaggerAuthorizationFilter>();
});

var app = builder.Build();
_ = app.Services.GetRequiredService<IReportPhotoStorage>();
app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var status = context.HttpContext.Response.StatusCode;
    await ApiErrors.WriteAsync(context.HttpContext, status, status == 404 ? "El recurso no existe." : "No se pudo procesar la solicitud.", context.HttpContext.RequestAborted);
});
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.Run();
