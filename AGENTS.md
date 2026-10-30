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
