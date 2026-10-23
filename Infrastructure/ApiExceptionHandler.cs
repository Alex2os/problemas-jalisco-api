using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace JaliscoProblemsAPI.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var (status, message) = exception switch
        {
            ReportOperationException failure => (failure.Status, failure.Message),
            BadHttpRequestException failure => (failure.StatusCode, "La carga no es válida o supera el tamaño permitido."),
            GeocodingUnavailableException => (503, "No se pudo obtener la ubicación. Intenta más tarde."),
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "comments_report_id_fkey" or "likes_report_id_fkey" }
                => (404, "El reporte no existe."),
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "comments_user_id_fkey" or "reports_user_id_fkey" or "likes_user_id_fkey" }
                => (401, "La sesión no corresponde a un usuario activo."),
            NpgsqlException { IsTransient: true } => (503, "El servicio de datos no está disponible. Intenta más tarde."),
            _ => (500, "Ocurrió un error al procesar la solicitud.")
        };
        if (status >= 500 && exception is not GeocodingUnavailableException)
            logger.LogError(exception, "Request failed with HTTP {StatusCode}; trace {TraceId}", status, context.TraceIdentifier);
        else
            logger.LogWarning("Request failed with HTTP {StatusCode}; trace {TraceId}", status, context.TraceIdentifier);
        await ApiErrors.WriteAsync(context, status, message, cancellationToken);
        return true;
    }
}
