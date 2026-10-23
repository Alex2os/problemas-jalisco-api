# Report photos and status tracking

## Database rollout

`schema.sql` is a complete, copy/paste-ready PostgreSQL setup/upgrade script. Open your application's database in Neon SQL Editor or pgAdmin, connect as the database owner, paste the entire file and run it. It works for a new database or the existing project schema and can be run again without removing stored data. `jalisco-problemas_schema.sql` contains the same SQL for compatibility with the old filename. Neither file contains psql-only commands, hosted role grants or credentials.

The API uses parameterized Npgsql SQL, not EF migrations. For an **existing database**, apply only:

```sh
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/migrations/001_report_photos_status.sql
```

Supply the connection securely using your existing deployment secret mechanism; never commit it. Back up the database before rollout. The migration is transactional and repeatable; it does not delete reports, likes, comments, users, or stored photos. It adds:

- `reports.status`, `status_updated_at` (UTC/timestamptz), and `status_version`.
- `report_status_history`, indexed uniquely by `(report_id, version)`; the database trigger writes history atomically with report insertion/status-version updates.
- `report_images`, containing uploader/report foreign keys, generated storage keys, byte size, JPEG content type, timestamp and display order. A checked slot of 1–3 plus `(report_id, display_order)` uniqueness enforces the maximum even under concurrent writes.

Old reports become `reported`. Their initial system history entry is marked `backfill`, has no claimed human actor, and records when tracking began. Existing timezone-less report timestamps are not reinterpreted. New reports record their actual owner and creation time. The full schema includes the same migration for new databases. The full `schema.sql` can also upgrade an existing project database; it uses conditional table creation and the same additive migration. Apply the migration before deploying this API; the app never runs migrations against your configured database on startup.

## Storage and deployment

Production requires `PhotoStorage__RootPath`, an absolute path to a durable, writable directory. Development defaults to `<content-root>/App_Data/report-photos` (Git-ignored). Configure a persistent disk/volume and grant the existing non-root container user write permission. Setting a path alone does not make an ephemeral container filesystem persistent. Back up and restore this directory together with the database. Do not expose it as a static-files directory.

The local filesystem implementation supports a single API instance, or instances with a genuinely shared persistent filesystem. Separate per-instance disks cannot serve one another's photos. A host without persistent disk support needs a storage adapter backed by durable storage before enabling this feature. No paid cloud service is configured or required by the code.

Every photo is served through `/reports/{report_id}/photos/{photo_id}/content` after a metadata lookup; deleted associations cannot expose leftover files. URLs are relative API paths, resolved by the mobile client against `EXPO_PUBLIC_API_URL`, without relying on backend localhost, Host headers or a device-specific address. Preserve that route/path prefix in your reverse proxy. Configure an HTTPS URL reachable from physical devices.

JPEG and PNG input is limited to 5 MiB per file, 12 megapixels and 6000 pixels per side; multipart requests are capped at 16 MiB. A managed decoder reads actual pixels, and the server generates a fresh JPEG (quality 85), removing filenames, EXIF/GPS and appended payloads. The two MIT/Unlicense image packages are used because ASP.NET Core has no cross-platform image decoder; no external image service is needed. At most two files are decoded simultaneously per process. The mobile picker converts device formats such as HEIC into bounded JPEGs (longest side 1800) before upload.

Creation with photos is one database transaction after all files are validated/written. Adds and deletes lock the report row, recheck ownership, and commit metadata atomically. Rejected/failed batches clean up unreferenced files. If the database commit outcome cannot be determined, files are retained and a warning is logged rather than risking deletion of committed data. A process crash or failed filesystem deletion can also leave inaccessible orphan files. Monitor disk space and reconcile older unreferenced keys against `SELECT storage_key FROM report_images`; do not delete referenced files or files belonging to active uploads. There is no automatic destructive cleanup job.

Requests use existing write rate limits. Adjust proxy request-body limits/timeouts to allow the documented upload limits. A timeout may occur after a commit: the UI instructs users to refresh before retrying. There is no cross-device idempotency key for report creation; check the refreshed feed before republishing an uncertain creation.

## Contracts

All existing JSON create/list/detail/search, likes and comments routes remain available. `POST /reports/create` additionally returns `report_id`. Every report adds `photos`, `status`, `status_updated_at`, `status_version` and server-computed `can_manage`.

| Method | Route | Contract |
| --- | --- | --- |
| POST | `/reports/create-with-photos` | Authenticated multipart: existing `type`, `description`, `latitude`, `longitude` fields and 1–3 repeated `photos` files. Returns `report_id` and full `report`. |
| POST | `/reports/{id}/photos` | Owner-only multipart, 1–3 files within the remaining capacity. Returns full `report`. |
| DELETE | `/reports/{id}/photos/{photoId}` | Owner only. Returns full `report`; missing photo 404. |
| GET | `/reports/{id}/photos/{photoId}/content` | Public JPEG; missing metadata/file 404, `nosniff`, no personalized HTTP cache. |
| GET | `/reports/{id}/status` | Public current status, updated timestamp, version and optional authenticated `can_manage`. |
| PUT | `/reports/{id}/status` | Owner only JSON `{ "status": "resolved", "expected_version": 0 }`; returns full `report`. |
| GET | `/reports/{id}/status/history` | Public, newest version first; `limit=1..100` (default 30), optional `beforeVersion`. Returns `history`, `has_more`, `next_before_version`. |

User IDs and actor IDs in submitted payloads are never trusted. There is no existing administrator/organization role to extend. All status/photo changes require an active authenticated user owning that report; another user receives 403, missing resources 404, invalid files/statuses 400, stale status versions/photo-capacity conflicts 409.

## Status rules

- `reported` → `still_present` or `resolved`.
- `still_present` → `resolved`, or reconfirm `still_present` with a new timestamp/history entry.
- `resolved` → `still_present` to reopen.
- Repeating the same `reported` or `resolved` with the current version is a no-op. Returning to `reported`, unknown statuses and `in_progress` are rejected.
- A stale `expected_version` is rejected instead of overwriting newer information. Current status/version/timestamp and trigger-generated audit entry commit or roll back together. Clients cannot edit history.

## Files

Added: `DTO/UpdateReportStatus.cs`, `database/migrations/001_report_photos_status.sql`, `Controllers/ReportMediaController.cs`, `Data/Repositories/ReportMediaRepository.cs`, `Infrastructure/ReportOperationException.cs`, `Models/ReportMedia.cs`, `Services/ReportPhotoStorage.cs`, `tests/JaliscoProblemsAPI.Tests/ReportMediaTests.cs`, and this document.

Modified: `.gitignore`, `.dockerignore`, `Controllers/Reports.cs`, `Data/Repositories/ReportRepository.cs`, `Infrastructure/ApiExceptionHandler.cs`, `JaliscoProblemsAPI.csproj`, `Models/ReportWithUsername.cs`, `Program.cs`, `jalisco-problemas_schema.sql`, `tests/integration.py`, and `README.md`.

See the mobile README for frontend files and native rebuild instructions.

## Verification

The existing xUnit suite is extended with actual pixel decoding/re-encoding, arbitrary/corrupt image rejection, dimension bounds and explicit status transitions. `tests/integration.py` creates a private disposable PostgreSQL cluster and photo directory, tests non-destructive backfill and repeated migration, owner/auth boundaries, concurrent uploads/status writes, persisted metadata/content/history after API restart, missing files, atomic failure cleanup and existing likes/comments.

The same integration suite invokes both mobile service integration scripts against real HTTP/database/disk, including three-photo creation, status history, fresh-client reopen, another authenticated user, deletion/addition and feed consistency. Only native platform/SecureStore and React Native multipart file bindings are emulated in Node. These tests do not launch a camera or render native UI. Run native acceptance on both platforms: denied/limited permissions, cancellation, camera/gallery selection, preview removal, large text/small screens, keyboard scrolling, fullscreen navigation, offline/slow uploads and OS Back/dismissal. Android and iOS exports validate bundling, not device interaction or native permission prompts.

Implementation references: [Expo ImagePicker](https://docs.expo.dev/versions/latest/sdk/imagepicker/), [StbImageSharp](https://github.com/StbSharp/StbImageSharp), and [StbImageWriteSharp](https://github.com/StbSharp/StbImageWriteSharp).

Validation executed for this implementation: .NET build (0 warnings/errors); 37 xUnit tests; all 16 isolated HTTP/PostgreSQL integration scenarios, including both real mobile-service scripts; frontend TypeScript/lint and all three Node test files; Android/iOS Expo exports; Expo development server startup and `/status` health response. No camera, gallery permission prompt or native screen was manually exercised on a phone/emulator. No configured application database was migrated.
