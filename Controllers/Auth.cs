using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.DTOs.Auth;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JaliscoProblemsAPI.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(UserRepository users, PasswordService passwords, TokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterUserRequestDTO request, CancellationToken cancellationToken)
    {
        var user = new User { Username = request.Username, Email = request.Email, PasswordHash = passwords.HashPassword(request.Password) };
        var result = await users.CreateUserAsync(user, cancellationToken);
        return result switch
        {
            CreateUserResult.EmailExists => ApiErrors.Result(409, "El email ya existe."),
            CreateUserResult.UsernameExists => ApiErrors.Result(409, "El nombre de usuario ya existe."),
            _ => Ok(new { message = "El usuario se ha creado con éxito." })
        };
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginUserRequestDTO request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (!passwords.CheckPassword(request.Password, user?.PasswordHash) || user is null)
            return ApiErrors.Result(401, "Los datos introducidos no son correctos.");
        return Ok(new
        {
            message = "Login completado con éxito.", token = tokens.CreateToken(user),
            user = new { id = user.Id, username = user.Username, email = user.Email }
        });
    }
}
