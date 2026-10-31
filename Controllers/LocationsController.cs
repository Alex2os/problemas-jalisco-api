using JaliscoProblemsAPI.Data.Repositories;
using JaliscoProblemsAPI.DTOs.Locations;
using JaliscoProblemsAPI.Infrastructure;
using JaliscoProblemsAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JaliscoProblemsAPI.Controllers;

[ApiController, Route("locations"), Authorize, EnableRateLimiting("geocoding")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LocationsController(GeocodingService geocoding, ReportRepository reports) : ControllerBase
{
    [HttpGet("reverse")]
    public async Task<IActionResult> Reverse([FromQuery] ReverseLocationQuery query, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("user_id")?.Value, out var userId)
            || userId <= 0 || !await reports.UserExistsAsync(userId, cancellationToken))
            return ApiErrors.Result(401, "Inicia sesión para continuar.");
        var location = await geocoding.ReverseAddressAsync(query.Latitude!.Value, query.Longitude!.Value, cancellationToken);
        return Ok(new
        {
            message = location.Available ? "La dirección se obtuvo con éxito." : "No se encontró una dirección para este punto.",
            location
        });
    }
}
