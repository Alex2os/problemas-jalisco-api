# AGENTS.md — ProblemasJalisco API

## Project overview

ProblemasJalisco API is an ASP.NET Core 10 / C# REST API serving a separate Expo / React Native frontend. It uses PostgreSQL through Npgsql, BCrypt password hashing, JWT authentication, reverse geocoding, and persistent report photo storage.

Inspect the current code, project files, README.md and relevant docs before changing behavior. Preserve compatibility with the mobile clients.

The product is a practical civic reporting tool, not a social network. Recommendations should help people discover relevant civic problems, not optimize engagement or popularity.

## Architecture

- Controllers/: HTTP routing, request validation, authentication and response composition.
- DTO/: request/response contracts. Follow existing namespaces even where folder and namespace names differ.
- Models/: domain and database result models.
- Data/ and Data/Repositories/: database configuration and concrete repositories using Npgsql.
- Services/: focused services for geocoding, passwords, tokens and report photo storage.
- Infrastructure/: shared API errors, exception handling, validation and Swagger support.
- Configuration/: typed configuration and startup validation.
- database/migrations/: versioned SQL migrations.
- tests/JaliscoProblemsAPI.Tests/: xUnit tests.
- tests/integration.py: real HTTP/PostgreSQL regressions against a disposable local database.
- docs/: feature contracts and deployment notes.

Prefer existing controllers, concrete repositories and focused services. Keep controllers thin where practical. Do not introduce EF Core, generic repositories, architectural layers, wrapper modules or packages without a clear need.

## C# conventions

- Use PascalCase for types, public members and C# filenames; camelCase for parameters and local variables.
- Follow existing namespace, DTO, async method and partial repository conventions.
- Keep nullable reference types enabled and handle absent data explicitly.
- Prefer readable, strongly typed code over dynamic values and speculative abstractions.
- Propagate CancellationToken through asynchronous database and external service operations.
- Update references, documentation and tests when moving or renaming public elements.

## API contracts

- Preserve existing routes, HTTP methods, success codes, JSON names and response envelopes unless the task explicitly changes them.
- Retain established snake_case fields, including legacy names such as ubication. C# naming conventions do not justify changing wire contracts.
- Prefer additive endpoints and optional fields for new features.
- Keep existing unpaginated report reads working; the frontend map relies on the full list.
- Validate IDs, coordinates, text lengths, page sizes and cursors. Use deterministic ordering with a unique tie-breaker for paginated results.
- Reuse ApiErrors and existing ProblemDetails handling, including the message extension used by the frontend.
- Return appropriate validation, unauthorized, forbidden, not-found and conflict responses. Do not expose stack traces or database details.
- Document new or changed endpoints, authentication, parameters, response shapes and fallback behavior in README.md or the relevant docs file.

## Report titles, search and categories

- Both JSON `POST /reports/create` and multipart `POST /reports/create-with-photos` require a user-written `title`: nonblank, single line, at most 80 UTF-16 code units. Preserve Spanish validation errors and validation before geocoding or photo storage. Trim surrounding whitespace before persisting.
- Titles are stored independently of descriptions and returned in the shared report model across lists, search, recommendations, details and writes returning reports. Descriptions remain optional with their existing 255-character limit.
- The description-excerpt/computed-title experiment was reverted. Do not derive or backfill titles from descriptions. Legacy reports retain null titles and use the frontend's category-based fallback.
- Migration `database/migrations/002_report_titles.sql` follows 001 and must run before the updated API. It adds `title varchar(80)` and a trigger requiring a nonblank single-line title on inserts or explicit title changes. Preserve unrelated updates to legacy reports with null titles. Keep both full schema scripts consistent and repeatable without rewriting existing data.
- There is no title-edit endpoint. The frontend title input is implemented on Evidencia, with required-field validation and an 80-character counter; the review screen displays the entered title. Backend and client creation requests must continue to agree on the required field.
- `/reports/search` matches `ubication`, `type`, `description` and stored `title` using parameterized PostgreSQL `ILIKE`. Preserve wildcard semantics, optional pagination, HTTP validation and null-description/title behavior. Do not claim frontend accent normalization also applies to PostgreSQL search.
- Adding a column to `ReportSelect` requires updating `ReadReport` and the recommendation reader's following column indexes. Preserve ranking, recommendation reasons, photo mapping and viewer permissions.
- The eight active categories are `baches`, `alumbrado`, `agua`, `basura`, `seguridad`, `arboles`, `semaforos`, `otro`, with existing legacy aliases. `semaforos` means Semáforos and needs no schema migration; preserve existing `otro` reports without reclassification. Banquetas y accesibilidad and a broader Señales category remain suggestions only. Do not add other categories or subcategories without a separate request.
- The frontend deliberately uses short local category subtitles instead of the API's longer explanations. Do not change the public catalog contract merely to style category cards.

## Authentication and authorization

- Derive user identity and ownership from validated JWT claims, never from client-provided user IDs.
- Enforce ownership and permissions on the server for report edits, status changes, photos and comment deletion.
- Preserve public reads and optional authenticated personalization where implemented.
- Preserve token claims, validation, expiration and password behavior unless explicitly asked to change them.
- Use existing authentication and write rate-limit policies where appropriate.
- Personalized responses must not leak across users through caches. Preserve existing no-store behavior.

## Recommended reports (Para ti)

- The frontend uses `GET /reports/recommended`. Read `docs/recommendations.md` before changing its contract or ranking.
- Current ranking is rule-based: unresolved status, proximity within 5 km when coordinates are supplied, relevant categories from the authenticated user's reports/confirmations, then recency and ID. Do not describe this as AI or silently substitute a popularity feed.
- Keep the existing report shape plus recommendation reasons, nullable distance, `has_more` and `next_offset`. Preserve bounded page sizes and deterministic ordering for an unchanged dataset.
- Preserve public access and useful fallbacks when location or user history is absent. User identity comes only from JWT claims; supplied user IDs must not personalize a guest request.
- Coordinates must be paired, finite and in range. For floating-point properties, use floating-point Range bounds; test NaN, infinity and fractional values just outside the bounds to prevent conversion exceptions and accidental acceptance.
- Location is request-scoped. Do not persist coordinates, add viewing history or log precise locations unnecessarily. Preserve no-store responses.
- Offset pagination can shift when data changes. Document that limitation and retain frontend deduplication; do not claim snapshot consistency.
- Keep future AI changes explicit, separately scoped and compatible with guest/error fallback.

## Report location address preview

- The frontend uses authenticated `GET /locations/reverse?latitude=...&longitude=...` to resolve a manually selected map pin. Read `docs/location-preview.md` before changing its contract.
- Require a valid token for an existing user. Both coordinates are required, finite and within the same Jalisco bounds as report creation. Preview is read-only and must not create a report or persist location history.
- Preserve the response envelope and structured fields, including echoed selected coordinates, `available`, nullable address components and formatted address. Never replace the selected coordinates with the provider's nearest feature or invent missing address components.
- Distinguish explicit no coverage (200 with `available: false`) from invalid input (400), authentication failure (401), rate limiting (429 with Retry-After), and provider failure/timeout (503).
- Preview and report creation share `GeocodingService`, its structured cache and outbound gate. Keep `ReverseAsync` compatible with existing report creation: return the bounded formatted string or fail without inserting a fabricated address.
- Preserve the bounded cache: successful results for 24 hours, explicit no coverage for five minutes, transient failures uncached. Cached address data is not user-specific; HTTP responses remain no-store.
- Keep the separate per-user geocoding rate policy and existing configurable limit. Preserve application-wide provider throttling, request cancellation, bounded waits and timeouts. Process-local gates do not coordinate multiple API instances.
- Device GPS permission is a frontend concern. Manual lookup must work without device-location access; do not add permission flags to the API contract.
- Use fake geocoders in tests. Cover structured/partial results, no coverage, cache reuse with report creation, invalid/non-finite coordinates, authentication, timeout and rate-limit behavior.

## Report location search

- Authenticated `GET /locations/search?query=...` supports explicit user-submitted place/address searches for reporting. Read `docs/location-search.md` before changing the contract. Frontend integration is now implemented on the Ubicación step through `LocationSearch`, `SearchLocations` and `createLocationSearch`; preserve compatibility with that client.
- Require an existing user, a nonblank query with at least three normalized characters and at most 200 raw UTF-16 code units, and return `{ message, locations }` with at most five named candidates and structured nullable address components. Preserve coordinates, provider ordering, deduplication and independent finite/bounds checks.
- Keep empty matches (200 with an empty array) distinct from malformed/provider/timeout failures (503), authentication failures (401), invalid input (400) and application rate limits (429 with Retry-After). Do not invent missing address components or store searches/reports as a side effect.
- Search shares `GeocodingService`, the bounded cache and outbound gate with reverse preview/report creation. Use separate normalized search cache keys; preserve successful/empty TTLs and uncached transient failures. Search must not populate reverse-coordinate cache entries from a candidate's address.
- Preserve the configurable provider, one-request-per-second application limit, timeout, cancellation, per-user geocoding policy and attribution. Public Nominatim forbids autocomplete: the frontend searches only on an explicit Buscar/keyboard submit action. Do not add keystroke requests, location permission requirements or query-history persistence.
- Keep fake-provider unit/integration coverage for mapping, no results, caching, authentication, malformed responses, retry recovery, timeout, rate limits and shared reverse/search throttling. Do not claim live-provider coverage from these tests.
- The frontend shows the search bar above a taller map without the removed bold search label. Choosing a candidate moves the adjustable pin; live reverse-address preview and explicit GPS remain in place. Failed/empty searches preserve the selected point. Do not require device-location permission, confirmation of a candidate as a report, or search-history persistence in the backend contract.
- Keep the client/server coordinate bounds aligned: latitude 18.92–22.75 and longitude -105.68–-101.50. This rectangle is not a municipality/state polygon check. Candidate addresses must not override the final adjusted pin or be written into the reverse-coordinate cache.
- In the search action, keep the complex query DTO parameter named `request` (or explicitly configure binding). Naming it `query` collides with the public `?query=...` key and can cause valid requests to fail model binding. Test the real HTTP route, not only DTO validation.
- Preserve the actual frontend service coverage in the disposable suite: an authenticated search, partial address fields, empty results and reverse lookup of the selected candidate. Do not infer live-provider accuracy or device interaction validation from those tests.

## Client-facing error behavior

- Keep sanitized Spanish errors and the existing top-level `message` extension so the shared frontend error/retry UI can display them.
- Do not convert provider outages, database failures or uncertain write completion into successful empty results. Preserve meaningful HTTP status codes.
- Maintain idempotency and concurrency protections; do not introduce automatic write retries that could duplicate reports or comments after a lost response.

## PostgreSQL and migrations

- Parameterize all untrusted SQL values. Do not interpolate request data into SQL.
- Reuse the DI-managed NpgsqlDataSource; dispose commands, readers, connections and transactions deterministically.
- Use transactions where multiple writes must succeed together. Preserve database constraints, concurrency protections, idempotent interactions and status history.
- Keep database connections free while waiting for unrelated external HTTP calls.
- Prefer additive, data-preserving migrations in database/migrations/.
- Keep schema.sql and jalisco-problemas_schema.sql consistent with supported setup/upgrade behavior when schema changes require it.
- Never run migrations, destructive SQL or tests against an application/production database without explicit authorization.
- Document migration order, required configuration and rollout steps. Do not silently provision databases or extensions.

## External services, uploads and configuration

- Use environment variables, .NET user secrets and existing configuration validation. Never hardcode credentials, tokens, private addresses or signing keys.
- Do not print secret values or commit real connection strings, environment files or uploaded data.
- Use IHttpClientFactory, bounded timeouts, cancellation and existing caching/throttling patterns for external calls.
- Preserve geocoding attribution, rate limits and failure handling. Tests should use fake services instead of public providers.
- Preserve photo validation, size/count limits, ownership, safe storage paths and configured persistent storage.
- Blank photo storage paths use the content-root default only in Development; other environments require an explicit absolute path.
- Failed-upload cleanup must check database references before deleting files. Keep its independent, bounded cancellation deadline so request cancellation does not bypass cleanup, and retain files when commit status is uncertain.
- Do not log sensitive report contents or precise user locations unnecessarily.
- If AI is added, keep provider credentials server-side, validate model output, bound requests/costs and provide a defined failure fallback. Do not treat model output as authorization or trusted SQL.

## Testing and validation

After meaningful code changes, run from the API repository:

```bash
dotnet restore JaliscoProblemsAPI.sln
dotnet build JaliscoProblemsAPI.sln --no-restore
dotnet test JaliscoProblemsAPI.sln --no-restore
git diff --check
```

For changes to SQL, HTTP contracts, authentication, permissions or persisted behavior, also run the applicable integration coverage:

```bash
python3 tests/integration.py
```

Read the integration test prerequisites in README.md first. The suite requires a built Debug API, an unprivileged Unix user and local PostgreSQL tools; it creates its own disposable database and fake geocoder. Never redirect it to a real application database.

For frontend integration, set `JALISCO_TEST_FRONTEND_DIR` to the actual frontend checkout rather than relying on the legacy default directory name. The client scripts must reference current service filenames. Report skipped checks separately; do not call them passing checks.

Add focused coverage for new behavior and meaningful regressions, including invalid input, permissions and fallback cases where relevant. Report checks that could not run and distinguish pre-existing failures from regressions. Documentation-only changes require review and git diff --check, not an application build.

## Test-data SQL

- When asked for copy-paste seed SQL, provide the script without executing it against the application database. A request for SQL is not authorization to run it.
- Seed reports need valid titles, coherent title/description/category combinations and existing user IDs. Destructive replacement must explicitly identify removal of related likes, comments, photo records and status history while preserving users. SQL deletion does not remove uploaded photo files from disk.
- For broadly spread map test data, use a grid with randomized positions rather than a single tight cluster. The latest requested area was an approximate urban ZMG rectangle (latitude 20.45–20.80, longitude -103.52–-103.15). Those are seed-data bounds, not a change to the API's Jalisco validation limits or a guarantee of street/municipal-boundary placement.

## Git and scope

- Inspect git status before editing and preserve unrelated uncommitted work.
- Do not reset, discard or overwrite changes unrelated to the task.
- Keep work focused; avoid speculative refactoring and dependency upgrades.
- Do not commit, push, rebase or force-push without explicit authorization.
- Do not change secrets, deployment configuration or the frontend repository unless explicitly requested.
- Explain significant design decisions, summarize changed files and report validation results and material limitations.

## Incremental delivery

- Work in agreed stages. Finish and validate a backend step, then report its results before starting the next stage or frontend integration.
- Continue routine, reversible API work already authorized by the user without repeatedly requesting confirmation. Environment/sandbox approvals, when required, still apply.
- Preserve existing architecture and contracts; “remake” or cleanup requests mean targeted, justified improvements unless the user explicitly requests a rewrite.
