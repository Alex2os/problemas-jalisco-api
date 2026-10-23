using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace JaliscoProblemsAPI.Services;

public sealed class GeocodingUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

// Singleton: public Nominatim permits at most one outbound request/second for this application instance.
public sealed class GeocodingService(IHttpClientFactory clients, ILogger<GeocodingService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private long? _lastRequestCompletedAt;

    public async Task<string> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var key = (latitude, longitude);
        if (_cache.TryGetValue(key, out string? cached) && cached is not null) return cached;
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken))
            throw new GeocodingUnavailableException("El servicio de ubicación está ocupado. Intenta de nuevo.");
        var requestStarted = false;
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null) return cached;
            if (_lastRequestCompletedAt.HasValue)
            {
                var delay = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastRequestCompletedAt.Value);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
            requestStarted = true;
            using var client = clients.CreateClient("Geocoding");
            var query = string.Create(CultureInfo.InvariantCulture, $"reverse?format=jsonv2&lat={latitude:R}&lon={longitude:R}&addressdetails=1");
            using var response = await client.GetAsync(query, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Reverse geocoder returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new GeocodingUnavailableException("No se pudo obtener la ubicación. Intenta más tarde.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("display_name", out var name)
                || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                throw new GeocodingUnavailableException("El servicio de ubicación devolvió una respuesta inválida.");

            // Fit the existing varchar(255) column without splitting a Unicode character.
            var location = new StringBuilder();
            foreach (var rune in name.GetString()!.EnumerateRunes().Take(255)) location.Append(rune.ToString());
            var result = location.ToString();
            _cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Reverse geocoder timed out");
            throw new GeocodingUnavailableException("El servicio de ubicación tardó demasiado. Intenta de nuevo.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning("Reverse geocoder failed with {FailureType}", exception.GetType().Name);
            throw new GeocodingUnavailableException("No se pudo obtener la ubicación. Intenta más tarde.", exception);
        }
        finally
        {
            // Spacing from completion also prevents network startup delays from bunching arrivals upstream.
            if (requestStarted) _lastRequestCompletedAt = Stopwatch.GetTimestamp();
            _gate.Release();
        }
    }

    public void Dispose() { _gate.Dispose(); _cache.Dispose(); }
}
