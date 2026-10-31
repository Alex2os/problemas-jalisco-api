# Location search for the report location step

## Endpoint

`GET /locations/search?query=Calle%20Morelos%20Guadalajara`

Requires a valid Bearer token for an existing user, like `/locations/reverse`. This read-only operation does not create reports, store search history or require device GPS access. Responses use `Cache-Control: no-store`.

`query` is required, with at most 200 UTF-16 code units before normalization and at least three after trimming/collapsing whitespace. Invalid input returns 400 with the existing Spanish ValidationProblemDetails `message`. Results are fixed at at most five; no pagination or optional user coordinates are needed.

```json
{
  "message": "Las ubicaciones se obtuvieron con éxito.",
  "locations": [
    {
      "name": "Escuela Morelos",
      "latitude": 20.67,
      "longitude": -103.35,
      "formatted_address": "Escuela Morelos, Calle Morelos 10, Centro, Guadalajara",
      "address": "Calle Morelos 10",
      "street": "Calle Morelos",
      "street_number": "10",
      "colonia": "Centro",
      "municipality": "Guadalajara"
    }
  ]
}
```

The example is illustrative, not a live-provider guarantee. `name` uses the provider's place name when available, otherwise the formatted address. Missing structured address components stay null. `address` uses the available street/number, otherwise the formatted address. Strings use the existing reverse-geocoding mapping and Unicode-safe 255-code-point component bounds. Search coordinates are the provider's candidate coordinates, not a confirmed report location.

Provider order is preserved. Duplicate coordinates are removed. Requests specify Mexico and the same rectangular bounds as report creation (latitude 18.92–22.75, longitude -105.68–-101.50); returned coordinates are independently checked for finite values and those bounds. This is not an administrative-boundary polygon check and does not guarantee street-level precision or results for every street, intersection or business.

## Empty results and errors

- 200 with `locations: []` means the provider returned no matches or all valid candidates were outside the allowed rectangle. The message is `No se encontraron ubicaciones para esta búsqueda.`
- 400 means invalid/missing input; 401 means missing/invalid authentication or a token for a nonexistent user.
- 429 with `Retry-After` means the existing per-user geocoding rate policy rejected the request. Search and reverse share that policy (default 30 requests/minute).
- 503 means the provider failed, timed out, returned an HTTP error (including upstream 429), unreadable JSON or malformed results, or the outbound gate was busy. It remains a sanitized error with a top-level Spanish `message`, never a successful empty search.
- Caller cancellation propagates; provider failures are not automatically retried or cached as empty results.

## Provider, caching and limits

Search reuses `GeocodingService`, the named `Geocoding` HTTP client, configurable `Geocoding:BaseUrl`/User-Agent, and the singleton outbound gate shared with address preview and report creation. The existing five-second HTTP timeout, three-second gate wait and at least one-second spacing after request completion apply. No new package, secret, deployment setting or database migration is required.

Search and reverse entries share the bounded 1,024-entry in-memory cache with distinct key types. Search keys collapse whitespace and ignore case while preserving accents. Nonempty results live for 24 hours, empty results for five minutes, and transient failures are uncached. Search does not fill the reverse-coordinate cache: selecting a candidate still lets the existing reverse preview resolve the selected pin. Caches/gates are process-local; replicas require coordinated throttling or a suitable provider. Do not log raw search text or enable query logging in deployment access logs.

The public Nominatim service is only suitable for moderate, explicit user-triggered searches. Its [usage policy](https://operations.osmfoundation.org/policies/nominatim/) requires an application-wide maximum of one request/second, identifying User-Agent, caching, attribution and provider switchability; autocomplete is forbidden. The developer remains responsible for that informed provider choice. This endpoint is not a generic geocoding service for unrelated consumers. [Search API documentation](https://nominatim.org/release-docs/latest/api/Search/).

## Frontend integration — next stage

The frontend has not been changed for this feature. Call this endpoint only when the user presses Buscar or submits the search keyboard action, never on each keystroke. Present candidates with their names and address details. Selecting one should move the map pin, then allow adjustment and explicit confirmation through the existing reporting flow. Keep the selected pin authoritative and the existing GPS permission, reverse lookup, retry and stale-response protections. Search failures or no results must preserve the current selected point. Keep OpenStreetMap attribution visible and do not add search-history persistence.

## Validation

Unit tests cover query validation, URL encoding, provider restrictions, mapping, partial fields, Unicode, finite/bounded coordinates, deduplication, result cap, concurrent cache reuse, empty results, malformed/provider/transport failures, recovery, timeout, cancellation, and shared reverse/search throttling. The disposable integration suite uses a fake geocoder and real HTTP/PostgreSQL to verify authentication, validation, JSON/no-store responses, caching, errors, recovery, shared per-user rate limits, and no report writes. No live-provider or native map validation is claimed.
