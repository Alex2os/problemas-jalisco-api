using Microsoft.AspNetCore.Mvc;

namespace JaliscoProblemsAPI.Infrastructure;

public static class ApiErrors
{
    public static ProblemDetails Create(int status, string message, string? traceId = null)
    {
        var problem = new ProblemDetails { Status = status, Title = message, Detail = message };
        problem.Extensions["message"] = message; // Existing Expo clients read this field.
        if (traceId is not null) problem.Extensions["traceId"] = traceId;
        return problem;
    }

    public static ObjectResult Result(int status, string message) => new(Create(status, message)) { StatusCode = status };

    public static async Task WriteAsync(HttpContext context, int status, string message, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(Create(status, message, context.TraceIdentifier),
            options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
    }
}
