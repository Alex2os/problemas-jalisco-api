using JaliscoProblemsAPI.Data.UserRepository;
using JaliscoProblemsAPI.Data.DataBaseConnection;
using JaliscoProblemsAPI.Services.PasswordService;
using JaliscoProblemsAPI.Services.TokenService;
using JaliscoProblemsAPI.Data.ReportRepository;

// used for authentication
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers();

builder.Services.AddScoped<DBConnection>(); // a singleton is used like a copy for the whole app or program. however, it's better to give a different connection each time a request is made, thats why we use scoped here too.
builder.Services.AddScoped<UserRepository>(); // a scoped is a copy for each instance, so for each request, then discards it after used.
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ReportRepository>();
// using scoped in the userepository (which are operations in users) is better as using a singleton (only one copy for the whole program) could share previous data from other transactions accidentally.

// with this AddAuthentication function and the AddJwtBearer in the program we can validate a jwt token sent in the Authorization header.
// inside the AddJwtBearer we specify the validation rules.
// in this case we specify what we are going to validate, that's why we use ValidateIssuer = true, ValidateAudience = true, etc.
// we also specify the valid issuer and the valid audience so the program can have something to check the token against.
// lastly, we specify the issuersigningkey, which is very important as we use the secret key we have here in the backend to validate the token too.

// basically the AddAuthentication tells the program how to validate/identify a user
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    // string JwtKey = builder.Configuration["Jwt:Key"];
    // changed to environment variable for now. with the launchSettings we are able to access all env. variables.
    string JwtKey = Environment.GetEnvironmentVariable("JWT_KEY");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(JwtKey)
        ),
    };
});

// the authorization tells the program how to decide if the user can access something.
// using AddAuthorization we can add the authorization policy services to the program.
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// we add the useauthentication and useauthorization after the builder.Build and before the app.MapControllers to properly add everything correctly.
app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();