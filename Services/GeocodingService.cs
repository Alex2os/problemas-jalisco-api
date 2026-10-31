using System.Diagnostics;
using System.Globalization;
using System.Net;
using JaliscoProblemsAPI.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var location = await ReverseAddressAsync(latitude, longitude, cancellationToken);
        return location.FormattedAddress ?? throw new GeocodingUnavailableException("No se pudo obtener la ubicación. Intenta más tarde.");
    }

    public async Task<GeocodedAddress> ReverseAddressAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (latitude, longitude);
        if (_cache.TryGetValue(key, out GeocodedAddress? cached) && cached is not null) return cached;
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
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                logger.LogWarning("Reverse geocoder returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new GeocodingUnavailableException("No se pudo obtener la ubicación. Intenta más tarde.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var payload = document.RootElement;
            if (payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String && error.GetString() == "Unable to geocode")
            {
                var missing = new GeocodedAddress(latitude, longitude, false, null, null, null, null, null, null);
                _cache.Set(key, missing, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
                return missing;
            }
            if (!response.IsSuccessStatusCode || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("display_name", out var name)
                || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
                throw new GeocodingUnavailableException("El servicio de ubicación devolvió una respuesta inválida.");

            var result = ReadAddress(payload, latitude, longitude);
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

    public async Task<IReadOnlyList<LocationSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Regex.Replace(query, @"\s+", " ").Trim();
        var key = "search:" + normalized.ToUpperInvariant();
        if (_cache.TryGetValue(key, out IReadOnlyList<LocationSearchResult>? cached) && cached is not null) return cached;
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
            // Explicit user searches only. Public Nominatim must not be used for autocomplete.
            var path = "search?format=jsonv2&addressdetails=1&accept-language=es&countrycodes=mx"
                + "&viewbox=-105.68,22.75,-101.50,18.92&bounded=1&limit=5&q=" + Uri.EscapeDataString(normalized);
            using var response = await client.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Location search geocoder returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new GeocodingUnavailableException("No se pudo buscar la ubicación. Intenta más tarde.");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Array || payload.GetArrayLength() > 40)
                throw new GeocodingUnavailableException("El servicio de ubicación devolvió una respuesta inválida.");
            var results = new List<LocationSearchResult>();
            var seen = new HashSet<(double, double)>();
            foreach (var item in payload.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryCoordinate(item, "lat", out var latitude) || !TryCoordinate(item, "lon", out var longitude)
                    || !item.TryGetProperty("display_name", out var displayName) || displayName.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(displayName.GetString()))
                    throw new GeocodingUnavailableException("El servicio de ubicación devolvió una respuesta inválida.");
                // Recheck the same coordinate bounds as report creation; never trust only the provider's viewbox.
                if (latitude < 18.92 || latitude > 22.75 || longitude < -105.68 || longitude > -101.50) continue;
                if (!seen.Add((latitude, longitude))) continue;
                var address = ReadAddress(item, latitude, longitude);
                results.Add(new LocationSearchResult(FirstPart(item, "name") ?? address.FormattedAddress!, latitude, longitude,
                    address.FormattedAddress!, address.Address!, address.Street, address.StreetNumber, address.Colonia, address.Municipality));
                if (results.Count == 5) break;
            }
            IReadOnlyList<LocationSearchResult> result = results.AsReadOnly();
            _cache.Set(key, result, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = results.Count > 0 ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(5)
            });
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Location search geocoder timed out");
            throw new GeocodingUnavailableException("El servicio de ubicación tardó demasiado. Intenta de nuevo.", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning("Location search geocoder failed with {FailureType}", exception.GetType().Name);
            throw new GeocodingUnavailableException("No se pudo buscar la ubicación. Intenta más tarde.", exception);
        }
        finally
        {
            if (requestStarted) _lastRequestCompletedAt = Stopwatch.GetTimestamp();
            _gate.Release();
        }
    }

    private static bool TryCoordinate(JsonElement item, string field, out double value)
    {
        value = 0;
        if (!item.TryGetProperty(field, out var raw)) return false;
        return (raw.ValueKind == JsonValueKind.Number ? raw.TryGetDouble(out value)
            : raw.ValueKind == JsonValueKind.String && double.TryParse(raw.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            && double.IsFinite(value);
    }

    private static GeocodedAddress ReadAddress(JsonElement payload, double latitude, double longitude)
    {
        var name = payload.GetProperty("display_name");
        // Keep the legacy formatted location compatible with varchar(255), without splitting Unicode.
        var formatted = new StringBuilder();
        foreach (var rune in name.GetString()!.EnumerateRunes().Take(255)) formatted.Append(rune.ToString());
        var address = payload.TryGetProperty("address", out var parts) && parts.ValueKind == JsonValueKind.Object
            ? parts : default;
        var street = FirstPart(address, "road", "pedestrian", "residential", "footway", "path");
        var number = FirstPart(address, "house_number");
        var streetAddress = street is null ? formatted.ToString() : string.Join(" ", new[] { street, number }.Where(value => value is not null));
        return new GeocodedAddress(latitude, longitude, true, formatted.ToString(), streetAddress, street, number,
            FirstPart(address, "suburb", "neighbourhood", "quarter", "city_district"),
            FirstPart(address, "municipality", "city", "town", "village", "county"));
    }

    private static string? FirstPart(JsonElement address, params string[] names)
    {
        if (address.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
            if (address.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
                return string.Concat(value.GetString()!.Trim().EnumerateRunes().Take(255).Select(rune => rune.ToString()));
        return null;
    }

    public void Dispose() { _gate.Dispose(); _cache.Dispose(); }
}
