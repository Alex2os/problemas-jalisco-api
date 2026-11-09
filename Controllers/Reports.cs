using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.DTOs.Reports;
using JaliscoProblemsAPI.DTOs.Reports.Comment;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JaliscoProblemsAPI.Controllers;

[ApiController]
[Route("reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(ReportRepository reports, GeocodingService geocoding) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("categories")]
    public IActionResult GetCategories()
    {
        return Ok(new
        {
            message = "Las categorías se obtuvieron con éxito.",
            categories = ReportCategories.ActiveCategories
        });
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [HttpPost("create")]
    public async Task<IActionResult> CreateReport(CreateReportRequestDTO request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        var location = await geocoding.ReverseAsync(request.Latitude, request.Longitude, cancellationToken);
        var reportId = await reports.CreateReportAsync(new Report
        {
            UserId = userId, Latitude = request.Latitude, Longitude = request.Longitude,
            Title = request.Title.Trim(),
            Description = request.Description ?? string.Empty,
            Type = ReportCategories.Normalize(request.Type),
            Ubication = location
        }, cancellationToken);
        return Ok(new { message = "El reporte se creó con éxito.", report_id = reportId });
    }

    [HttpGet("{report_id}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportById([Range(1, int.MaxValue)] int report_id, CancellationToken cancellationToken)
    {
        var report = await reports.GetByIdAsync(report_id, ViewerId, cancellationToken);
        return report is null ? ApiErrors.Result(404, "El reporte no existe.")
            : Ok(new { message = "El reporte se obtuvo con éxito.", report });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllReports([FromQuery, Range(1, 200)] int? limit, [FromQuery, Range(1, int.MaxValue)] int? beforeId, CancellationToken cancellationToken)
    {
        var items = await reports.GetReportsAsync(null, limit, beforeId, ViewerId, cancellationToken);
        return Ok(new { message = "Los reportes se obtuvieron con éxito.", reports = items });
    }

    [Authorize]
    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedReports([FromQuery, Range(1, 200)] int limit = 20,
        [FromQuery, Range(1, int.MaxValue)] int? beforeId = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        var items = await reports.GetReportsAsync(null, limit + 1, beforeId, userId, cancellationToken, savedOnly: true);
        var hasMore = items.Count > limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return Ok(new
        {
            message = "Los reportes guardados se obtuvieron con éxito.", reports = items,
            has_more = hasMore, next_before_id = hasMore ? (int?)items[^1].Id : null
        });
    }

    [Authorize, EnableRateLimiting("write"), HttpPut("{report_id}/saved")]
    public Task<IActionResult> SaveReport([Range(1, int.MaxValue)] int report_id, CancellationToken cancellationToken)
        => ChangeSaved(report_id, true, cancellationToken);

    [Authorize, EnableRateLimiting("write"), HttpDelete("{report_id}/saved")]
    public Task<IActionResult> UnsaveReport([Range(1, int.MaxValue)] int report_id, CancellationToken cancellationToken)
        => ChangeSaved(report_id, false, cancellationToken);

    private async Task<IActionResult> ChangeSaved(int reportId, bool saved, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, ct))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        await reports.SetSavedAsync(reportId, userId, saved, ct);
        return Ok(new { message = saved ? "Reporte guardado." : "Reporte eliminado de guardados.", report_id = reportId, saved_by_me = saved });
    }

    [HttpGet("search")]
    public async Task<IActionResult> GetFilteredReports([FromQuery, Required, StringLength(255)] string query,
        [FromQuery, Range(1, 200)] int? limit, [FromQuery, Range(1, int.MaxValue)] int? beforeId, CancellationToken cancellationToken)
    {
        var items = await reports.GetReportsAsync(query, limit, beforeId, ViewerId, cancellationToken);
        return Ok(new { message = "Los reportes filtrados se obtuvieron con éxito.", reports = items });
    }

    [HttpGet("recommended")]
    public async Task<IActionResult> GetRecommendedReports([FromQuery] RecommendedReportsQuery query, CancellationToken cancellationToken)
    {
        var items = await reports.GetRecommendedReportsAsync(ViewerId, query.Latitude, query.Longitude,
            query.Limit + 1, query.Offset, cancellationToken);
        var hasMore = items.Count > query.Limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return Ok(new
        {
            message = "Los reportes recomendados se obtuvieron con éxito.",
            reports = items.Select(item => item.Report),
            recommendations = items.Select(item => item.Recommendation),
            has_more = hasMore,
            next_offset = hasMore ? (int?)(query.Offset + query.Limit) : null
        });
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [HttpPost("{report_id}/comments")]
    public async Task<IActionResult> CreateComment([Range(1, int.MaxValue)] int report_id, CreateCommentRequestDTO request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        if (!await reports.ReportExistsAsync(report_id, cancellationToken)) return ApiErrors.Result(404, "El reporte no existe.");
        var comment = await reports.CreateCommentAsync(new Comment { UserId = userId, ReportId = report_id, UserComment = request.UserComment.Trim() }, cancellationToken);
        return Ok(new { message = "Se creó el comentario con éxito.", comment, interactions = await reports.GetInteractionsAsync(report_id, userId, cancellationToken) });
    }

    [HttpGet("{report_id}/comments")]
    public async Task<IActionResult> GetCommentsByReportId([Range(1, int.MaxValue)] int report_id,
        [FromQuery, Range(1, 200)] int? limit, [FromQuery, Range(1, int.MaxValue)] int? afterId, CancellationToken cancellationToken)
    {
        if (!await reports.ReportExistsAsync(report_id, cancellationToken)) return ApiErrors.Result(404, "El reporte no existe.");
        var comments = await reports.GetCommentsAsync(report_id, limit.HasValue ? limit.Value + 1 : null, afterId, ViewerId, cancellationToken);
        var hasMore = limit.HasValue && comments.Count > limit.Value;
        if (hasMore) comments.RemoveAt(comments.Count - 1);
        return Ok(new
        {
            message = "Los comentarios se obtuvieron con éxito mediante el id.", comments,
            has_more = hasMore, next_after_id = hasMore ? (int?)comments[^1].Id : null,
            interactions = await reports.GetInteractionsAsync(report_id, ViewerId, cancellationToken)
        });
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [HttpPut("{report_id}/like")]
    public Task<IActionResult> LikeReport([Range(1, int.MaxValue)] int report_id, CancellationToken cancellationToken)
        => ChangeLike(report_id, true, cancellationToken);

    [Authorize]
    [EnableRateLimiting("write")]
    [HttpDelete("{report_id}/like")]
    public Task<IActionResult> UnlikeReport([Range(1, int.MaxValue)] int report_id, CancellationToken cancellationToken)
        => ChangeLike(report_id, false, cancellationToken);

    private async Task<IActionResult> ChangeLike(int reportId, bool liked, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        if (!await reports.ReportExistsAsync(reportId, cancellationToken)) return ApiErrors.Result(404, "El reporte no existe.");
        await reports.SetLikeAsync(reportId, userId, liked, cancellationToken);
        var interactions = await reports.GetInteractionsAsync(reportId, userId, cancellationToken);
        return interactions is null ? ApiErrors.Result(404, "El reporte no existe.")
            : Ok(new { message = liked ? "Me gusta guardado." : "Me gusta eliminado.", interactions });
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [HttpDelete("{report_id}/comments/{comment_id}")]
    public async Task<IActionResult> DeleteComment([Range(1, int.MaxValue)] int report_id, [Range(1, int.MaxValue)] int comment_id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId) || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "El usuario no está autenticado.");
        if (!await reports.ReportExistsAsync(report_id, cancellationToken)) return ApiErrors.Result(404, "El reporte no existe.");
        var result = await reports.DeleteCommentAsync(report_id, comment_id, userId, cancellationToken);
        if (result == DeleteCommentResult.Forbidden) return ApiErrors.Result(403, "Solo puedes eliminar tus propios comentarios.");
        if (result == DeleteCommentResult.NotFound) return ApiErrors.Result(404, "El comentario no existe.");
        return Ok(new { message = "Comentario eliminado.", comment_id, interactions = await reports.GetInteractionsAsync(report_id, userId, cancellationToken) });
    }

    private int? ViewerId => User.Identity?.IsAuthenticated == true && TryGetUserId(out var userId) ? userId : null;

    private bool TryGetUserId(out int userId) => int.TryParse(User.FindFirst("user_id")?.Value, out userId) && userId > 0;
}
