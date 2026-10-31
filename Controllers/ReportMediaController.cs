using System.ComponentModel.DataAnnotations;
using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.DTOs.Reports;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Models;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace JaliscoProblemsAPI.Controllers;

[ApiController, Route("reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportMediaController(ReportRepository reports, IReportPhotoStorage storage, GeocodingService geocoding, ILogger<ReportMediaController> logger) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirst("user_id")?.Value, out var id) ? id : 0;
    private int? Viewer => User.Identity?.IsAuthenticated == true ? UserId : null;

    private async Task RequireUserAsync(CancellationToken ct)
    {
        if (UserId <= 0 || !await reports.UserExistsAsync(UserId, ct))
            throw new ReportOperationException(401, "Inicia sesión para continuar.");
    }

    private async Task RequireOwnerAsync(int id, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(id, Viewer, ct);
        if (report is null) throw new ReportOperationException(404, "El reporte no existe.");
        if (report.UserId != UserId) throw new ReportOperationException(403, "Solo el autor puede modificar este reporte.");
    }

    private async Task<List<StoredPhoto>> SaveFilesAsync(List<IFormFile> photos, CancellationToken ct)
    {
        if (photos.Count is < 1 or > 3) throw new ReportOperationException(400, "Selecciona entre 1 y 3 fotos.");
        var stored = new List<StoredPhoto>();
        try
        {
            foreach (var photo in photos) stored.Add(await storage.SaveAsync(photo, ct));
            return stored;
        }
        catch
        {
            foreach (var photo in stored) storage.Delete(photo.Key);
            throw;
        }
    }

    private async Task CleanupUncommittedAsync(List<StoredPhoto> stored)
    {
        // Cleanup must survive request cancellation, but must not wait indefinitely.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var photo in stored)
        {
            try
            {
                if (!await reports.IsPhotoReferencedAsync(photo.Key, cleanup.Token)) storage.Delete(photo.Key);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Retaining photo after uncertain database commit; reconciliation required for {StorageKey}", photo.Key);
            }
        }
    }

    [Authorize, EnableRateLimiting("write"), HttpPost("create-with-photos")]
    [RequestSizeLimit(16 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateReportRequestDTO request, [FromForm] List<IFormFile> photos, CancellationToken ct)
    {
        await RequireUserAsync(ct);
        var location = await geocoding.ReverseAsync(request.Latitude, request.Longitude, ct);
        var stored = await SaveFilesAsync(photos, ct);
        try
        {
            var id = await reports.CreateWithPhotosAsync(new Report
            {
                UserId = UserId,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Title = request.Title.Trim(),
                Description = request.Description ?? string.Empty,
                Type = ReportCategories.Normalize(request.Type),
                Ubication = location
            }, stored, ct);
            return Ok(new { message = "Reporte y fotos publicados.", report_id = id, report = await reports.GetByIdAsync(id, UserId, ct) });
        }
        catch { await CleanupUncommittedAsync(stored); throw; }
    }

    [Authorize, EnableRateLimiting("write"), HttpPost("{report_id}/photos")]
    [RequestSizeLimit(16 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
    public async Task<IActionResult> Add([Range(1, int.MaxValue)] int report_id, [FromForm] List<IFormFile> photos, CancellationToken ct)
    {
        await RequireUserAsync(ct);
        await RequireOwnerAsync(report_id, ct);
        var stored = await SaveFilesAsync(photos, ct);
        try { await reports.AddPhotosAsync(report_id, UserId, stored, ct); }
        catch { await CleanupUncommittedAsync(stored); throw; }
        return Ok(new { message = "Fotos guardadas.", report = await reports.GetByIdAsync(report_id, UserId, ct) });
    }

    [Authorize, EnableRateLimiting("write"), HttpDelete("{report_id}/photos/{photo_id}")]
    public async Task<IActionResult> Delete([Range(1, int.MaxValue)] int report_id, [Range(1, int.MaxValue)] int photo_id, CancellationToken ct)
    {
        await RequireUserAsync(ct);
        var key = await reports.DeletePhotoAsync(report_id, photo_id, UserId, ct);
        storage.Delete(key); // Logical delete commits first; inaccessible orphan is safe if filesystem cleanup fails.
        return Ok(new { message = "Foto eliminada.", report = await reports.GetByIdAsync(report_id, UserId, ct) });
    }

    [HttpGet("{report_id}/photos/{photo_id}/content")]
    public async Task<IActionResult> Content([Range(1, int.MaxValue)] int report_id, [Range(1, int.MaxValue)] int photo_id, CancellationToken ct)
    {
        var key = await reports.GetPhotoKeyAsync(report_id, photo_id, ct);
        if (key is null) return ApiErrors.Result(404, "La foto no está disponible.");
        try
        {
            // Open before returning so removal between lookup and streaming becomes a clean 404.
            var stream = new FileStream(storage.PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(stream, "image/jpeg", enableRangeProcessing: true);
        }
        catch (FileNotFoundException) { return ApiErrors.Result(404, "La foto no está disponible."); }
        catch (DirectoryNotFoundException) { return ApiErrors.Result(404, "La foto no está disponible."); }
    }

    [HttpGet("{report_id}/status")]
    public async Task<IActionResult> Status([Range(1, int.MaxValue)] int report_id, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(report_id, Viewer, ct);
        return report is null ? ApiErrors.Result(404, "El reporte no existe.") : Ok(new { status = report.Status, status_updated_at = report.StatusUpdatedAt, status_version = report.StatusVersion, can_manage = report.CanManage });
    }

    [HttpGet("{report_id}/status/history")]
    public async Task<IActionResult> History([Range(1, int.MaxValue)] int report_id, [FromQuery, Range(1, 100)] int limit = 30, [FromQuery, Range(1, int.MaxValue)] int? beforeVersion = null, CancellationToken ct = default)
    {
        if (!await reports.ReportExistsAsync(report_id, ct)) return ApiErrors.Result(404, "El reporte no existe.");
        var history = await reports.GetStatusHistoryAsync(report_id, beforeVersion, limit + 1, ct);
        var more = history.Count > limit;
        if (more) history.RemoveAt(history.Count - 1);
        return Ok(new { history, has_more = more, next_before_version = more ? (int?)history[^1].Version : null });
    }

    [Authorize, EnableRateLimiting("write"), HttpPut("{report_id}/status")]
    public async Task<IActionResult> Update([Range(1, int.MaxValue)] int report_id, UpdateReportStatusRequestDTO request, CancellationToken ct)
    {
        await RequireUserAsync(ct);
        await reports.ChangeStatusAsync(report_id, UserId, request.Status, request.ExpectedVersion!.Value, ct);
        return Ok(new { message = "Estado guardado.", report = await reports.GetByIdAsync(report_id, UserId, ct) });
    }
}
