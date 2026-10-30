# Para ti — recommended reports

## Endpoint

`GET /reports/recommended` is public and accepts optional Bearer authentication. The existing report list, search and detail contracts are unchanged. No migration or new configuration is required.

Query parameters:

| Parameter | Default | Validation |
| --- | --- | --- |
| latitude | omitted | Finite number, -90 through 90; requires longitude |
| longitude | omitted | Finite number, -180 through 180; requires latitude |
| limit | 20 | 1 through 50 |
| offset | 0 | Nonnegative integer, at most `int.MaxValue - 50` |

Successful responses contain:

```json
{
  "message": "Los reportes recomendados se obtuvieron con éxito.",
  "reports": [],
  "recommendations": [],
  "has_more": false,
  "next_offset": null
}
```

`reports` contains the existing report shape, including photos, status and viewer permissions. Each corresponding `recommendations` entry contains `report_id`, `reason` and nullable `distance_km`. Empty pages return empty arrays and no next offset. Invalid parameters return the existing 400 ValidationProblemDetails shape with `message`.

## Ranking and reasons

Ranking is deterministic for an unchanged dataset, in this priority order:

1. Unresolved reports (`reported` or `still_present`) before resolved reports.
2. Reports within 5 km of the supplied location before other reports.
3. Categories appearing in reports the authenticated user created or currently confirmed before other categories.
4. Most recent creation date, missing dates last, then descending report ID to break ties.

This is rule-based relevance, not AI inference. There is no popularity score or collection of viewing/search history. Guest reads ignore submitted identity values; identity comes only from validated JWT claims. With no history, category preference is absent. With no coordinates, proximity is absent. Without either signal, ranking uses unresolved status, recency and ID.

Reasons are stable codes for frontend labels:

| Code | Suggested label |
| --- | --- |
| nearby_and_related | Cerca de ti y relacionado con tus reportes |
| nearby | Cerca de ti |
| related_category | Relacionado con tus reportes |
| recent | Reporte reciente |

`distance_km` is the approximate great-circle distance, rounded to two decimal places, or null when coordinates are omitted. The unrounded distance determines the 5 km threshold. Reasons explain relevance signals; they do not claim an official priority or severity assessment.

## Pagination and privacy

Pass `next_offset` as the next request's `offset`, keeping location and authentication unchanged. A refresh starts at offset zero. Offset pagination can shift when reports, statuses or confirmations change between requests; clients should deduplicate by report ID. This version does not promise a frozen feed snapshot.

Responses use the report controller's existing no-store cache policy. The endpoint does not persist the supplied location, return user interest profiles, or modify reports. Deployment access logs should not retain precise location query strings unnecessarily.

## Validation

Unit coverage checks query defaults, paired/finite coordinates and pagination bounds. The disposable PostgreSQL/HTTP integration suite checks routing, validation, guest fallback, proximity/category ranking, token-based ownership, confirmation signals, reasons, distances, no-store responses and pagination.
