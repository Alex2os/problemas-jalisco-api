# Problemas Jalisco API

ASP.NET Core 10 / C# 14 Web API for the Expo mobile app. Uses PostgreSQL through Npgsql, BCrypt passwords, and HS256 JWTs. Controllers, concrete repositories, and focused services are retained; no EF Core, generic repository, or application-layer framework is introduced.

## Configuration and startup

Use environment variables or .NET user secrets. Never put real signing keys, passwords, or connection strings in committed files. `Properties/launchSettings.json` remains ignored and is not used by Docker or `dotnet <assembly>`.

| Setting | Purpose |
| --- | --- |
| `JWT_KEY` or `Jwt__Key` | HS256 signing key, at least 32 UTF-8 bytes. If both are set, they must match. |
| `Jwt__Issuer`, `Jwt__Audience` | Required token issuer and audience (existing appsettings values remain supported). |
| `ConnectionStrings__DefaultConnection` | Optional standard Npgsql connection string. Takes precedence over the legacy DB variables. |
| `DB_HOST`, `DB_USERNAME`, `DB_PASSWORD`, `DB_DATABASE`, `DB_PORT` | Existing database configuration remains supported. Port must be 1–65535. |
| `Geocoding__BaseUrl` | Default `https://nominatim.openstreetmap.org/`; must be HTTPS and end with `/`. Development permits HTTP loopback for tests. |
| `Geocoding__UserAgent` | Application identity, default `ProblemasJaliscoAPI/1.0`. Configure a contact URL/email for your deployment. |
| `RateLimiting__AuthPermitLimit` | Default 20 combined login/register requests per remote IP per minute. |
| `RateLimiting__WritePermitLimit` | Default 30 report/comment writes per authenticated user per minute. |
| `Cors__AllowedOrigins__0`, etc. | Explicit browser origins, without trailing slashes. No wildcard/credential policy. Native Expo requests do not require CORS. |

Configuration is validated at startup without printing values. Creating a data source does not open a database connection; database availability is checked when used. Tokens retain the existing `user_id`, `user_email`, `username` claims, seven-day expiration, and framework-default five-minute clock skew. Set the shared signing key to the same key previously used for issuance to retain valid existing tokens. A deliberate key rotation invalidates old tokens.

```sh
dotnet restore JaliscoProblemsAPI.sln
dotnet build JaliscoProblemsAPI.sln --no-restore
dotnet test JaliscoProblemsAPI.sln --no-restore
dotnet run --project JaliscoProblemsAPI.csproj --no-launch-profile --urls http://localhost:5140
```

Set secrets and environment before startup; the commands above contain no credentials. The mobile client uses `EXPO_PUBLIC_API_URL` to locate this API. Development exposes Swagger UI at `/swagger`, its document at `/swagger/v1/swagger.json`, and the existing built-in document at `/openapi/v1.json`. Swagger marks authenticated operations and supports the Bearer authorization control.

## Contracts and compatibility

| Method | Route | Authentication | Successful response |
| --- | --- | --- | --- |
| POST | `/auth/register` | Public | 200 `{ message }` |
| POST | `/auth/login` | Public | 200 `{ message, token, user: { id, username, email } }` |
| GET | `/reports` | Public | 200 `{ message, reports }`, newest ID first |
| GET | `/reports/search?query=...` | Public | 200 `{ message, reports }` |
| GET | `/reports/{report_id}` | Public | 200 `{ message, report }` |
| POST | `/reports/create` | Bearer | 200 `{ message, report_id }` |
| GET | `/reports/{report_id}/comments` | Public | 200 `{ message, comments, interactions, has_more, next_after_id }`, oldest ID first |
| POST | `/reports/{report_id}/comments` | Bearer | 200 `{ message, comment, interactions }` |
| PUT / DELETE | `/reports/{report_id}/like` | Bearer | 200 `{ message, interactions }` |
| DELETE | `/reports/{report_id}/comments/{comment_id}` | Bearer, comment owner | 200 `{ message, comment_id, interactions }` |

Report/comment JSON keeps all existing snake_case fields, including `ubication`, `created_at`, `user_id`, `report_id`, and `user_comment`. Ownership always comes from the validated JWT; a submitted user ID is ignored. Viewing reports and commenting on other people's reports remain supported. Only the authenticated author can delete a comment. Likes belong to the authenticated user; repeated like/unlike requests are safe.

Optional `limit=1..200` and `beforeId=<positive ID>` provide keyset pagination for reports and search. Optional `limit=1..200` and `afterId=<positive ID>` paginate comments. Omit pagination to retain the existing full response used by the mobile map. Search continues to use PostgreSQL `ILIKE` wildcard semantics; values remain parameterized.

Errors use ProblemDetails/ValidationProblemDetails with the existing top-level `message` extension so the mobile client can still display them. Validation includes `errors`; unexpected failures include a trace ID rather than exception details. Success status codes and envelopes are retained. Intentional corrections:

- Missing reports, including comment access on missing reports, return 404 instead of an empty report or successful empty list. Invalid positive-ID/query bounds return 400.
- Registration limits usernames to 20 characters, matching the supplied database schema, rather than accepting 21–30 and failing during insertion.
- New registration passwords are limited to 72 UTF-8 bytes because BCrypt otherwise ignores trailing bytes. The existing 8-character minimum remains. Login still accepts the original maximum of 100 characters to avoid locking out legacy accounts; existing hashes are not changed. Existing accounts with oversized BCrypt passwords need a planned reset/migration.
- Duplicate email/username inserts return 409 even when concurrent requests race. Credential errors remain generic 401 responses.
- Rate limits can return 429 with `Retry-After`. A geocoder failure/timeout returns 503 without inserting a partial report. Geocoder results are bounded to the existing 255-character location column, without splitting Unicode characters.
- Nullable legacy descriptions are returned as empty text. A missing stored timestamp is returned as `null`; the updated mobile client displays “Fecha no disponible”. No timezone is invented for existing timestamps.

## Reliability and external calls

A DI-owned singleton `NpgsqlDataSource` manages pooling. Repositories acquire resources per command, use async readers/commands, dispose them deterministically, and propagate request cancellation. Database operations do not hold an open connection while waiting for reverse geocoding. Inserts are single statements protected by existing unique/foreign-key constraints; no new transaction abstraction is needed.

The geocoder uses `IHttpClientFactory`, a five-second outbound timeout, invariant coordinate formatting, a bounded 1,024-entry/24-hour success cache, and a singleton gate spacing requests at least one second apart. Waiting for a busy gate is capped at three seconds. Request cancellation is propagated and failures do not produce a bogus location. The public [Nominatim policy](https://operations.osmfoundation.org/policies/nominatim/) requires application-wide rate limits, caching, and attribution. These controls are process-local: multiple replicas require coordinated throttling or a different/self-hosted provider. Do not treat this as sufficient for unrestricted public-service use.

HTTPS redirection is retained. Deploy behind correctly configured TLS termination; configure trusted proxy addresses and forwarded headers in the deployment before relying on remote-IP limits or HTTPS scheme forwarding. This change does not blindly trust `X-Forwarded-For`. Rate limits are process-local and should be reviewed for proxy/NAT traffic.

Docker publishes only the API project and runs under the .NET image's non-root app user. Tests, build outputs, Git history, environment files, and launch secrets are excluded from the build context. No database schema or stored data is changed by application startup or this refactor.

## Testing

The xUnit project adds only test dependencies (test SDK, xUnit, runner). It covers JWT/configuration compatibility, password verification, UTF-8/column validation, coordinate bounds, JSON contracts, ProblemDetails, geocoder caching/throttling, Unicode, upstream failures, timeouts, and cancellation.

Additional real HTTP/database regressions use Python's standard library:

```sh
python3 tests/integration.py
```

Run after building Debug output, as an unprivileged Unix user with PostgreSQL server/client tools (`pg_config`, `initdb`, `pg_ctl`, `psql`). The script creates a private cluster under `/tmp` with Unix-socket-only access, imports the existing schema there, starts the API with synthetic credentials/explicit local connection settings, and uses a loopback fake geocoder. Cleanup stops those processes and removes only its own temporary directory. It never uses your configured database or calls the public geocoder. The tests include real unique/FK constraints, response contracts, auth spoofing/invalid JWTs, pagination, small-pool concurrency, rate limits, CORS, and sanitized database failures.

## Remaining work, in priority order

1. Confirm deployment secret/proxy/TLS settings, add persistent monitoring, and plan revocation/refresh and reset handling for legacy oversized passwords. No token-lifetime or hashing migration is applied automatically.
2. Before adding replicas, coordinate geocoding limits/cache and rate limits or move to a suitable provider. The mobile About screen includes OpenStreetMap attribution; verify attribution visibility and provider requirements for your final distribution.
3. Profile production queries before changing schema. The comments lookup is a candidate for `(report_id, id)` indexing; substring location search may eventually benefit from an explicitly reviewed trigram index. No indexes/extensions/migrations were applied.
4. Plan viewport fetching/pagination in the mobile map before changing the backward-compatible unbounded default. Large datasets can still produce large responses when callers omit pagination.
5. Plan timestamp-with-time-zone and case-insensitive account normalization migrations only after checking existing data/timezone/collisions. The current schema and matching semantics are preserved.
6. `bin/` and `obj/` are ignored now, but already tracked artifacts remain tracked. Review and remove them from Git separately without deleting developer work.

Implementation follows [Npgsql data-source/disposal guidance](https://www.npgsql.org/doc/basic-usage.html) and [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).

## Persistent likes and comments

Existing PostgreSQL `likes` and `comments` tables in `jalisco-problemas_schema.sql` are reused without migrations or data changes. The composite likes primary key prevents duplicate likes, including concurrent requests. Comments retain the existing 100-character limit and support creation, reading, and owner-only deletion; editing is not introduced.

Reports add `like_count`, `comment_count`, and `liked_by_me`. Public reads accept optional Bearer authentication and disable HTTP caching of personalized responses. Comment responses add server-computed `can_delete`; paginated reads provide `has_more` and `next_after_id`. All interaction responses provide authoritative counts. Existing routes, fields, success codes, and unpaginated reads remain compatible.

Backend feature files: `Controllers/Reports.cs`, `Data/Repositories/ReportRepository.cs`, `Models/ReportWithUsername.cs`, `Models/CommentWithUsername.cs`, new `Models/ReportInteractions.cs`, `Infrastructure/ApiExceptionHandler.cs`, and `Program.cs`. Regression coverage is in `tests/integration.py` and `tests/JaliscoProblemsAPI.Tests/ContractTests.cs` (see the tests directory for the actual project layout).

The integration suite additionally verifies concurrent/idempotent likes, ownership, comment pagination/deletion, database persistence across API restart, and the sibling mobile application's actual API services against this private database. Set `JALISCO_TEST_API_DLL` to test an alternate build output and `JALISCO_TEST_FRONTEND_DIR` if the mobile repo is elsewhere. The mobile service test simulates reopening with fresh module/cache state while retaining the real API token. It does not render a native screen.

Deploy both updated projects using the existing connection string/JWT settings and mobile `EXPO_PUBLIC_API_URL`. No production database provisioning or migration is performed by these tests.

## Report photos and status

Reports now support up to three persistent photos and owner-managed `reported`, `still_present`, and `resolved` states with versioned history. **Deploy the additive SQL migration and configure a persistent photo directory before running the updated API in production.** See [rollout, endpoint contracts, storage limits, files and tests](docs/report-photos-status.md). No migration has been run against your configured application database.

## Copy/paste database setup

Open **schema.sql**, copy the entire file into your PostgreSQL query editor (for example Neon SQL Editor or pgAdmin), and run it as the database owner. It creates a new database's tables or upgrades this project's existing tables, preserving data. It is safe to run again against the expected project schema. No terminal commands, credentials or hosted role names need to be edited. The old `jalisco-problemas_schema.sql` filename contains the same query-editor-compatible SQL.

This configures database tables only; the API still requires its connection/JWT settings and a durable `PhotoStorage__RootPath` in production.

## Photo storage reliability

In Development, an unset, empty or whitespace-only `PhotoStorage:RootPath` uses `App_Data/report-photos` under the content root. Other environments still require an explicit absolute path. Failed photo writes check database references before deleting files; these checks use a separate five-second cleanup deadline, independent of request cancellation. Files are retained when a database commit cannot be confirmed.

## Recommended reports (Para ti)

`GET /reports/recommended` adds optional location/category relevance with guest fallback and bounded pages. Existing report endpoints are unchanged. See [ranking, parameters, response fields and pagination limitations](docs/recommendations.md).
