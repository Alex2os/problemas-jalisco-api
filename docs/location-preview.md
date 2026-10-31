# Address preview for the report location step

## Contract

`GET /locations/reverse?latitude=20.67&longitude=-103.35`

Requires a valid Bearer token for an existing user. No device GPS permission is involved: coordinates may come from a manually positioned map pin. This lookup does not create reports or persist the selected location. Responses use `Cache-Control: no-store`.

Both coordinates are required, finite numbers. Bounds match report creation: latitude 18.92–22.75 and longitude -105.68–-101.50. Invalid/missing values return 400 ValidationProblemDetails with the existing `message` extension; missing/invalid authentication returns 401.

Example successful response:

```json
{
  "message": "La dirección se obtuvo con éxito.",
  "location": {
    "latitude": 20.67,
    "longitude": -103.35,
    "available": true,
    "formatted_address": "Calle Morelos 10, Centro, Guadalajara",
    "address": "Calle Morelos 10",
    "street": "Calle Morelos",
    "street_number": "10",
    "colonia": "Centro",
    "municipality": "Guadalajara"
  }
}
```

The response echoes the selected coordinates, not the coordinates of the provider's nearest feature. Address labels are approximate: the frontend should keep the chosen pin authoritative and let the user confirm it.

Missing components are null. Street numbers are not inferred. `address` uses street plus number where available, otherwise the formatted address. Colonia falls back through suburb, neighbourhood, quarter and city district; municipality through municipality, city, town, village and county. Provider classifications vary and may be incomplete; state/country are never substituted as a municipality.

Explicit provider no-coverage responses (`Unable to geocode`, HTTP 200 or 404) return 200 with `available: false`, the requested coordinates and null address fields. Unexpected errors, malformed results and provider timeouts return the existing sanitized 503 error. Rate-limit rejection returns 429 with `Retry-After`.

## Shared geocoding and limits

Preview and report creation use the same singleton service, exact-coordinate cache and global outbound gate. Explicit [location search](location-search.md) also shares the bounded cache, outbound gate and per-user geocoding rate policy, with separate search cache keys. Successful structured results are cached in memory for 24 hours, explicit no-coverage for five minutes, with the existing 1,024-entry bound. Provider failures are not cached. Existing report creation still receives its formatted string, bounded to 255 Unicode characters, and still fails rather than inserting a fabricated address when lookup fails.

`RateLimiting:GeocodingPermitLimit` (environment key `RateLimiting__GeocodingPermitLimit`) defaults to 30 requests per authenticated user per minute. It is separate from the write limiter; changing it does not increase provider throughput. Existing five-second provider timeout, three-second gate wait, cancellation and application-identifying User-Agent remain in place.

The existing default public Nominatim service permits at most one request per second across the whole application, requires attribution and caching, and is intended for moderate, user-triggered usage. See the [usage policy](https://operations.osmfoundation.org/policies/nominatim/) and [reverse-geocoding limitations](https://nominatim.org/release-docs/latest/api/Reverse/). The gate/cache are process-local; multiple API instances need coordinated throttling or a suitable provider. Keep the provider configurable. Do not enable high-frequency continuous map lookups or log precise coordinate query strings unnecessarily.

## Frontend integration (separate step)

The frontend is unchanged by this backend feature. Replace native reverse geocoding with this endpoint for the address preview, keep debounce/stale-response protection, and request an address after map movement completes rather than on every movement event. Only the explicit GPS action should ask for device-location permission. Preserve retry/error behavior and map selection when no address is available. Keep required provider attribution visible.

No database migration, new dependency or secret is required. Coverage includes DTO validation, structured parsing, incomplete addresses, shared cache, no coverage, provider recovery, timeouts, endpoint authentication/rate limits, read-only preview and existing report creation.
