# File Storage Service

A production-shaped file storage service: ASP.NET Core 8 (Clean Architecture) API +
SQL Server + local filesystem storage, with an Angular 17 frontend, built for the
attached Full-Stack Developer Assessment. Built as if it were going into production
within the assessment's stated 8–12 hour scope — see `docs/architecture-decisions.md`
for the reasoning behind every non-obvious choice, and `docs/tooling-report.md` for an
honest account of which of the requested Claude Code tools were actually used.

## Contents

- [Prerequisites](#prerequisites)
- [Quick start with Docker](#quick-start-with-docker)
- [Local development](#local-development)
- [Configuration & environment variables](#configuration--environment-variables)
- [Database migrations](#database-migrations)
- [Demo login and role behavior](#demo-login-and-role-behavior)
- [Architecture overview](#architecture-overview)
- [API summary & examples](#api-summary--examples)
- [Storage layout](#storage-layout)
- [Streaming approach](#streaming-approach)
- [Database/filesystem consistency strategy](#databasefilesystem-consistency-strategy)
- [Security & preview decisions](#security--preview-decisions)
- [Error responses](#error-responses)
- [Tests & verification status](#tests--verification-status)
- [100 MB upload verification](#100-mb-upload-verification)
- [Assumptions](#assumptions)
- [Known limitations](#known-limitations)
- [Future enhancements](#future-enhancements)

## Prerequisites

- .NET 8 SDK (developed against SDK 10.0.301 building `net8.0` targets — the `net8.0`
  runtime must be installed; do not substitute another target framework)
- Node.js 20+ and npm, Angular CLI (the repo pins Angular **17** regardless of any
  globally installed newer CLI — see `frontend/package.json`)
- SQL Server reachable from the API: either SQL Server LocalDB (Windows, used for local
  dev and the integration test suite in this repo) or the Docker Compose profile's SQL
  Server 2022 Linux container
- Docker + Docker Compose, for the containerized path (see [Known
  limitations](#known-limitations) — this could not be executed in the environment this
  was built in, so treat the compose files as reviewed-but-not-execution-verified)

## Quick start with Docker

```bash
cp .env.example .env
# edit .env: set SA_PASSWORD and JWT_SIGNING_KEY to real values (see comments in the file)

docker compose up --build
```

- Frontend: http://localhost:4200
- API directly (bypassing the frontend proxy): http://localhost:5080
- SQL Server: localhost:1433 (exposed for inspection; the API talks to it via the
  `sqlserver` service name over the compose network)

The API container runs `Database.Migrate()` on startup (`Database__AutoMigrate=true` in
`docker-compose.yml`) — no manual migration step is needed for this profile. Uploaded
files persist in the `storage_data` named volume and SQL Server data in
`sqlserver_data`; both survive `docker compose down` (without `-v`) and container
recreation.

**This has not been executed end-to-end in the environment this was built in.** As of
2026-09-17 the Docker CLI is installed (`docker --version` → 29.8.0) but the Docker
Desktop engine/daemon is not reachable from this shell, so `docker compose up --build`
could not actually be run. The Dockerfiles and compose file were written carefully and
reviewed, but "the documented startup flow works without undocumented manual fixes"
remains unverified. See [Known limitations](#known-limitations) for exactly what was
and wasn't verified, and the commands to verify it yourself.

## Local development

Run SQL Server, the API, and the Angular dev server separately.

**1. Database** — SQL Server LocalDB (Windows) is simplest for local dev:

```powershell
sqllocaldb start mssqllocaldb
```

`src/FileStorage.Api/appsettings.Development.json` already points at
`(localdb)\mssqllocaldb`, database `FileStorageDb`.

**2. Backend:**

```bash
dotnet restore
dotnet ef database update --project src/FileStorage.Infrastructure --startup-project src/FileStorage.Api
dotnet run --project src/FileStorage.Api
```

The API listens on the port in `src/FileStorage.Api/Properties/launchSettings.json`
(`http://localhost:5028` by default). `appsettings.Development.json` already sets a
dev-only JWT signing key and `Jwt:EnableMockTokenEndpoint=true`.

**3. Frontend:**

```bash
cd frontend
npm install
npm start   # ng serve, with proxy.conf.json forwarding /api and /health to :5028
```

Visit http://localhost:4200.

## Configuration & environment variables

All backend configuration is standard ASP.NET Core (`appsettings.json` +
`appsettings.{Environment}.json` + environment variables using the `Section__Key`
convention). Key settings:

| Key | Purpose | Default |
|---|---|---|
| `ConnectionStrings:Default` | SQL Server connection string | *(empty — must be set)* |
| `Jwt:SigningKey` | HMAC signing key for mock JWTs, 32+ chars | *(empty — startup fails fast if unset/short)* |
| `Jwt:Issuer` / `Jwt:Audience` | JWT validation parameters | `file-storage-service` / `file-storage-clients` |
| `Jwt:EnableMockTokenEndpoint` | Gates `/api/auth/*` | `false` |
| `Storage:RootPath` | Filesystem storage root (outside web root) | `_storage` |
| `UploadPolicy:MaxFileSizeBytes` | Max upload size, decimal bytes | `209715200` (200 MiB) |
| `UploadPolicy:AllowedExtensions` | Extension allowlist | see `appsettings.json` |
| `UploadPolicy:PreviewAllowedContentTypes` | Inline-preview content-type allowlist | images + `application/pdf` |
| `Database:AutoMigrate` | Run migrations on startup | `false` (`true` in the Docker profile) |
| `Cors:AllowedOrigin` | Angular dev-server origin for CORS | *(empty)* |

Frontend: `frontend/src/environments/environment*.ts` sets `apiBaseUrl` to the relative
`/api` — same-origin through the dev proxy or the Docker nginx proxy, so no CORS is
needed in the container profile.

## Database migrations

Migrations live in `src/FileStorage.Infrastructure/Persistence/Migrations` and are
committed (not `EnsureCreated`). To create a new migration or apply migrations
manually:

```bash
dotnet ef migrations add <Name> --project src/FileStorage.Infrastructure --startup-project src/FileStorage.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/FileStorage.Infrastructure --startup-project src/FileStorage.Api
```

The Docker Compose profile instead sets `Database__AutoMigrate=true`, which calls
`AppDbContext.Database.Migrate()` once at API startup — that's this project's
documented migration-application mechanism for that profile specifically.

## Demo login and role behavior

The assessment asks for "a login screen generating a mock JWT (no real auth backend
needed)." This is implemented as: the Angular login screen calls a small,
explicitly-gated backend endpoint that signs a real JWT for one of two **fixed** demo
identities — there is no password, registration, or user store. See
`docs/architecture-decisions.md` §8 for the full reasoning and §12 for the session
storage trade-off.

- **user** (`Sign in as User`): can upload files, and access/soft-delete their own
  files.
- **admin** (`Sign in as Admin`): can access every file (any owner), and can
  hard-delete a file **that has already been soft-deleted**.

`Jwt:EnableMockTokenEndpoint` must be `true` for `/api/auth/*` to respond (404
otherwise) — true by default in `appsettings.Development.json` and in the Docker
Compose profile, false in the base `appsettings.json`.

## Architecture overview

```
FileStorage.Api  ───────────►  FileStorage.Application  ───────►  FileStorage.Domain
      │                                    ▲
      └────────► FileStorage.Infrastructure ┘
```

- **Domain** has no dependency on anything else in the solution.
- **Application** depends only on Domain (interfaces/ports it defines, DTOs, use-case
  services, validation, `UploadPolicyOptions`).
- **Infrastructure** depends on Application + Domain, and implements Application's
  ports (`IStoredObjectRepository` via EF Core, `IFileStorage` via the local
  filesystem).
- **Api** depends on Application directly for its business logic, and on
  Infrastructure only for composition (`AddInfrastructure` in `Program.cs`) — no
  controller or endpoint references an Infrastructure type directly.

See `docs/architecture-decisions.md` §1–§2 for the layer responsibilities and the one
significant deviation from "every endpoint is a controller" (the upload endpoint, and
why).

## API summary & examples

| Method | Path | Auth | Notes |
|---|---|---|---|
| `POST` | `/api/files` | user/admin | multipart/form-data, streamed |
| `GET` | `/api/files` | user/admin | paginated + filtered list, always excludes soft-deleted |
| `GET` | `/api/files/deleted` | admin | paginated soft-deleted files — server-backed, survives refresh/new sessions |
| `GET` | `/api/files/{id}` | user/admin | metadata (supporting endpoint for the preview page) |
| `GET` | `/api/files/{id}/download` | user/admin | attachment, Range-capable |
| `GET` | `/api/files/{id}/preview` | user/admin | inline, images + PDF only |
| `DELETE` | `/api/files/{id}` | user/admin | soft delete |
| `DELETE` | `/api/files/{id}/hard` | admin | hard delete, requires prior soft delete |
| `POST` | `/api/upload-sessions` | user/admin | create a resumable upload session |
| `GET` | `/api/upload-sessions/{id}` | owner/admin | session status + `nextExpectedOffset` |
| `PUT` | `/api/upload-sessions/{id}/chunks?offset=n` | owner | append one chunk (raw body) |
| `POST` | `/api/upload-sessions/{id}/finalize` | owner | complete the session into a normal file |
| `DELETE` | `/api/upload-sessions/{id}` | owner | abort, discards the partial upload |
| `GET` | `/api/admin/audit-log` | admin | paginated + filtered audit trail |
| `GET` | `/health/live` | none | process liveness only |
| `GET` | `/health/ready` | none | DB + filesystem readiness |
| `POST` | `/api/auth/mock-token` | none | dev/demo only, see above |
| `GET` | `/api/auth/demo-identities` | none | dev/demo only |

ETag: `GET`/`HEAD /api/files/{id}/download` and `/preview` return a strong `ETag` (the
file's own SHA-256, quoted) and `Last-Modified` (its `CreatedAtUtc`), and honor
`If-None-Match` (→ `304`, empty body), `If-Range`, and `Cache-Control: private, no-cache`
(never cacheable by a shared/proxy cache). Authorization is always checked before any
conditional-request short-circuit, so a stale ETag for a file that has since been
hard-deleted (or that belongs to someone else) still `404`s — it never `304`s.

Get a token and upload a file:

```bash
TOKEN=$(curl -s -X POST http://localhost:5028/api/auth/mock-token \
  -H "Content-Type: application/json" -d '{"role":"user"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

curl -X POST http://localhost:5028/api/files \
  -H "Authorization: Bearer $TOKEN" \
  -F "tags=invoice,2026" \
  -F "file=@report.pdf;type=application/pdf"
```

List with filters and pagination:

```bash
curl "http://localhost:5028/api/files?name=report&tag=invoice&contentType=application/pdf&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN"
```

Listing semantics: `name` is a substring match on the original filename; `tag` matches
one exact normalized tag (see [Storage layout](#storage-layout) tag-matching note);
`from`/`to` filter on `CreatedAtUtc` with `from` inclusive and `to` exclusive as passed
— pass the *next* day's date as `to` to include a full day's uploads. Sorting is
`CreatedAtUtc` descending with `Id` descending as a deterministic tie-breaker, so
pagination never skips or repeats a row even when many files share a timestamp.

Range download:

```bash
curl -H "Authorization: Bearer $TOKEN" -H "Range: bytes=0-99" \
  http://localhost:5028/api/files/{id}/download -D -
# HTTP/1.1 206 Partial Content, Content-Range: bytes 0-99/<total>
```

## Storage layout

```
_storage/
  yyyy/MM/dd/{key}/content.bin      # the actual file content
  _tmp/{guid}.tmp                   # in-flight uploads, same volume as final paths
  _health/probe-{guid}.tmp          # /health/ready filesystem round-trip probes
```

`{key}` is a 22-character, cryptographically random, base64url-encoded, server-generated
string — never a client-supplied filename, and never the database row's GUID `Id`. The
per-key directory means two files uploaded on the same calendar day can never collide.
See `docs/architecture-decisions.md` §3 for the full reasoning, and §4 for how tags are
normalized/deduplicated/stored to avoid accidental-substring tag matches.

## Streaming approach

- Uploads are parsed with `MultipartReader` directly against `Request.Body` — no
  `IFormFile` model binding, no buffering the whole file into memory or a
  `MemoryStream`. `FileSystemStorage.StageAsync` reads into a single reusable `byte[]`
  (`UploadPolicy:CopyBufferSizeBytes`, default 80 KB) in a loop, hashing incrementally
  with `IncrementalHash` (SHA-256) and writing each chunk to a temp file, counting bytes
  as it goes so the configured size cap is enforced **during** the transfer, independent
  of any `Content-Length` header.
- Upload is a stage/commit split: the file section streams to a temp file as soon as
  it's reached (`StageAsync`), but is only committed to its final path
  (`CommitAsync`, an atomic rename) after the *entire* multipart request has been
  read. This means `tags` fields may appear before or after the file part — order is
  not significant — since nothing is finalized until every section has been consumed.
  See `docs/architecture-decisions.md` §11.
- Downloads use ASP.NET Core's built-in `PhysicalFileResult` with
  `enableRangeProcessing: true` — 200/206/416 and `Content-Range` are all handled by
  the framework, not a hand-rolled Range parser.
- Storage keys are 128-bit random values; a collision is astronomically unlikely but
  not treated as impossible — `CommitAsync` detects an occupied destination and never
  overwrites it (`StorageKeyCollisionException`), and `UploadFileService` retries with
  a fresh key up to 5 times using the same already-staged bytes (no re-streaming).
  Verified by `FileSystemStorageTests.CommitAsync_WhenDestinationAlreadyOccupied_...`
  and `UploadFileServiceTests.FinalizeAsync_RetriesWithNewKey_WhenFirstGeneratedKeyCollides`.
- See `docs/architecture-decisions.md` §2 for why the upload endpoint is implemented as
  a Minimal API rather than an MVC controller action — a simplicity preference, not a
  framework limitation (a working controller-based alternative is documented there
  too).

## Database/filesystem consistency strategy

Summarized here; full detail is in `docs/architecture-decisions.md` §5: atomic
temp-file-then-rename writes, synchronous compensation (delete the file) if the
database write fails immediately after, and an acknowledged crash window if the
process dies between the filesystem commit and the database write — SQL Server and the
filesystem are never in one transaction, and this implementation does not claim
otherwise. Recovery from that window is the `--reconcile` command described next.

## Database/filesystem reconciliation

`--reconcile` is an offline administrative command, not a background job — run it
manually or from a scheduler after a suspected crash, or periodically as a health
check:

```bash
dotnet FileStorage.Api.dll --reconcile                        # report only, no mutation
dotnet FileStorage.Api.dll --reconcile --delete-orphans        # also delete confirmed orphan files
dotnet FileStorage.Api.dll --reconcile --grace-minutes=30      # override the default 15-minute grace window
```

It walks the storage tree once, streams every `StoredObjects` row's key from the
database, and cross-references the two:

- **Orphan files** — present on disk, no matching database row. Recently-written files
  (within `--grace-minutes`, default 15) are excluded from the *report* so a file
  that's mid-upload (already renamed into its final path, database write not yet
  committed) is unlikely to show up as a false positive. **This age check is a
  reporting heuristic, not a safety guarantee** — file age alone cannot prove a file is
  inactive (a stalled or unusually slow upload could still exceed the grace window).
  The actual safety mechanism for deletion is described below.
- **Missing content** — a database row (soft-deleted or not — soft-deleted rows are
  expected to still have content) whose file is absent.

**Default behavior is report-only: no file is deleted and no database row is ever
modified**, regardless of flags. Missing-content rows are never auto-fixed by this
command under any flag, because recovering one requires a human decision: restore the
file from a backup if one exists, or soft/hard-delete the row via the API once the
content is confirmed unrecoverable.

**`--delete-orphans` requires exclusive access to the storage root**, enforced by an
OS-level file lock (`_storage/_maintenance.lock`, `FileShare.None`) — not by the age
heuristic above. The API process acquires this lock for its entire serving lifetime as
soon as it starts. `--delete-orphans` attempts to acquire the same lock before deleting
anything; if it can't (because the API, or another reconcile process, currently holds
it), it refuses to delete anything, prints the reason, and exits `4`. This is a real,
verifiable exclusivity mechanism scoped to *this application's own writers* — it does
not, and cannot, prevent an unrelated process (a backup tool, a second differently
configured deployment pointed at the same volume, a human with shell access) from
writing to the storage root concurrently; that is a documented scope boundary, not a
gap in the lock's correctness within its scope.

**Verified with a staged scenario** during this session:
1. Two real files were uploaded through the running API; one file's physical content
   was then deleted manually (simulating "missing content"), and a third, unrelated
   file was placed directly on disk with no database row and backdated an hour
   (simulating an "orphan"). `--reconcile` reported exactly one orphan and one
   missing-content row, correctly excluding the healthy third file.
2. `--reconcile --grace-minutes=-5` was run: rejected outright with exit code `1`
   ("--grace-minutes must not be negative"), rather than silently accepting a value
   that would defeat the grace-period heuristic entirely.
3. **With the API still running** (holding the lock), `dotnet FileStorage.Api.dll
   --reconcile --delete-orphans` was run against the same storage root: it printed
   `SAFETY CHECK FAILED: could not acquire exclusive access...`, exited `4`, and the
   orphan file was confirmed still present on disk afterward.
4. The API was stopped, releasing the lock. The identical `--reconcile
   --delete-orphans` command was re-run: it printed `Exclusive access acquired...`,
   deleted exactly the one orphan file, and exited `5` (orphan successfully deleted;
   the missing-content row still requires human review, unaffected by this exit code).
   The healthy file's content was confirmed byte-identical throughout.

This scenario, and the reconciliation code itself, were revised once after an initial
implementation: a `/code-review` pass (see `docs/tooling-report.md`) found that
`UploadFileService.FinalizeAsync`'s compensating delete excluded
`OperationCanceledException` (a client disconnect after a successful commit but during
the database write would have orphaned the file with no cleanup), that
`MaintenanceLock.TryAcquire` only caught `IOException` and would crash on
`UnauthorizedAccessException` instead of degrading gracefully, and that a negative
`--grace-minutes` was accepted without validation. All three were fixed and
re-verified as shown above.

Exit codes: `0` clean (nothing found), `3` inconsistencies found and not remediated
this run (report-only, or `--delete-orphans` requested but no orphans existed to
delete), `4` `--delete-orphans` refused because exclusive access could not be verified,
`5` `--delete-orphans` succeeded and deleted one or more orphan files this run
(missing-content rows, if any, still require separate manual review regardless), `1` on
a usage error (e.g. a negative `--grace-minutes`).

## Security & preview decisions

- Filesystem paths are built only from the server-generated `Key` + `CreatedAtUtc`,
  never from user input; `StoragePathResolver` validates the key format and verifies
  the resolved path is contained within the storage root.
- Original filenames are sanitized (`FilenameSanitizer`) before being used in any
  response or header: path components and control characters (including CR/LF, which
  would otherwise enable header injection) are stripped, and length is bounded.
- Client-supplied `Content-Type` is stored as declared metadata but never trusted as
  proof of actual file contents; extension is checked against a configurable allowlist.
- Inline preview is restricted to `image/png`, `image/jpeg`, `image/gif`, `image/webp`,
  and `application/pdf` (configurable) — HTML and SVG are never served inline. A
  content type outside the allowlist gets `415 Unsupported Media Type` from
  `/preview`, with the Angular UI showing a "download instead" fallback.
- All downloads set `X-Content-Type-Options: nosniff` and arbitrary downloads are
  always `Content-Disposition: attachment`.
- Cross-user access returns 404 (not 403) to avoid confirming another user's file
  exists — see `docs/architecture-decisions.md` §6 for the one deliberate exception
  (403 for a non-admin hitting hard-delete).
- Storage directories (`_storage/`) must not be writable by untrusted users/processes —
  in Docker this is a dedicated named volume mounted only into the API container.

## Error responses

All errors are RFC 7807 `application/problem+json`, produced centrally by
`ExceptionHandlingMiddleware` (plus a matching `InvalidModelStateResponseFactory` for
model-binding 400s). Every response includes `type`, `title`, `status`, `instance`,
`traceId`, and `correlationId`; validation failures additionally include a field→
messages `errors` map. 5xx responses never include the underlying exception message —
only 4xx responses do, since those are considered safe to describe to the caller.

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Conflict",
  "status": 409,
  "detail": "File must be soft-deleted before it can be hard-deleted.",
  "instance": "/api/files/…/hard",
  "traceId": "0HNOJ…",
  "correlationId": "b0bbe8da…"
}
```

Status usage: `400` invalid request data / multipart shape violations, `401` missing or
invalid JWT, `403` non-admin hard-delete attempt, `404` missing or inaccessible
resource, `409` genuine state conflicts (repeated soft-delete, premature hard-delete),
`413` upload exceeds `UploadPolicy:MaxFileSizeBytes`, `415` unsupported extension or
unsupported preview content type, `500` unexpected failures. An ordinary client
disconnect (request cancellation initiated by the client) is logged at `Information`
and never surfaced as a 500.

## Tests & verification status

Commands a reviewer can run, and what actually happened when they were run here:

| Check | Command | Result |
|---|---|---|
| Backend restore + build | `dotnet build` (solution root) | **Passed** — 0 errors, 0 warnings across all 6 projects |
| Backend unit tests | `dotnet test tests/FileStorage.UnitTests` | **Passed — 61/61, re-run and confirmed 2026-09-17** (filename sanitization, tag normalization/matching, domain rules, path containment, streaming stage/discard + size-cap cleanup, storage-key collision detection + retry, cancellation-compensation regression, SHA-256 correctness, health probe, reconciliation orphan/missing-content matching + grace-period exclusion, maintenance-lock acquire/conflict/release, resumable-upload expiry cleanup + concurrent-chunk-append serialization, audit-write-failure never propagates to the calling operation) |
| Backend integration tests | `dotnet test tests/FileStorage.IntegrationTests` | **Passed — 45/45, re-run and confirmed 2026-09-17** against **real SQL Server LocalDB** (not EF Core InMemory) with real migrations applied per test run; covers upload/download byte integrity, both multipart field orders (tags before/after the file part), pagination/filters including UTC-correct date-range boundaries, cross-user 404, admin access, soft-delete lifecycle including the two-step hard-delete conflict, the server-backed admin deleted-files listing surviving a fresh-client "reload" and rejecting non-admins, oversized upload 413, missing-file-part 400, Range 206 and 416, ETag/conditional-request semantics, a readiness-probe genuine-failure case, correlation-ID-in-application-log-entry verification, and the full resumable-upload protocol including a genuine cross-process restart-recovery test. This run is current, dated evidence that the resumable-upload, audit-log, and ETag-caching suites pass — it supersedes any earlier note that those suites were unverified. |
| Reconciliation command | `dotnet run --project src/FileStorage.Api -- --reconcile [--delete-orphans]` | **Passed** — verified against a staged scenario (see "Database/filesystem reconciliation" above): correctly reported one orphan file and one missing-content row while excluding a third healthy file; `--delete-orphans` was refused with exit code `4` while the API held the maintenance lock (orphan confirmed untouched), then succeeded once the API was stopped, deleting only the orphan and leaving the healthy file byte-identical and the missing-content row still flagged |
| Maintenance lock unit tests | `dotnet test tests/FileStorage.UnitTests --filter FullyQualifiedName~MaintenanceLock` | **Passed** — 3/3, verifying acquire/conflict/release semantics of the OS-level exclusive lock directly |
| Frontend production build | `cd frontend && npx ng build --configuration production` | **Passed** |
| Frontend unit tests | `cd frontend && npx ng test --watch=false --browsers=ChromeHeadless` | **Passed — 11/11, re-run and confirmed 2026-09-17** (`AuthService` session lifecycle, the auth interceptor's URL-scoped header attachment, `FilesService` query building / filename parsing / 415-as-non-error preview handling — the suite grew from 8 to 11 specs as resumable-upload frontend code was added) |
| End-to-end tests | `cd tests/e2e && npx playwright test` (with the API and `ng serve` both running) | **Passed — 3/3 at the time these specs were authored.** Not re-run in the 2026-09-17 documentation pass — `tests/e2e/test-results/` is empty, so there is no fresh execution evidence for that date; treat the E2E result as carried forward, not re-verified today. Covers: (1) login → upload via file input → appears in list → download → SHA-256-verified byte match; (2) admin login → upload → soft delete → **reload the page** → find the file via the server-backed admin "Deleted files" panel → hard delete → row gone, confirmed on a second reload; (3) an ordinary user sees no deleted-files panel in the UI and a direct authenticated call to `GET /api/files/deleted` returns 403 |
| Docker Compose build/up | `docker compose up --build` | **Blocked — not run, rechecked 2026-09-17.** The Docker CLI is now present (`docker --version` → `Docker version 29.8.0`), so the earlier "Docker not installed" finding no longer holds — but the daemon is not reachable: `docker images` fails with `request returned 500 Internal Server Error ... dockerDesktopLinuxEngine/_ping`, i.e. Docker Desktop's engine isn't running/accessible from this shell. `docker compose config` does parse `docker-compose.yml` successfully (no syntax errors), but no image was built and no container was started. "The documented startup flow works without undocumented manual fixes" remains unverified. See [Known limitations](#known-limitations). |

Manual verification performed in addition to the above: `curl`-driven smoke tests of
every endpoint (upload, list, download, Range, preview unsupported-type fallback,
soft/repeated-soft/hard delete sequencing, cross-user and admin access, unauthenticated
401) during development, and a `claude-in-chrome` visual check of the login page, the
authenticated file list (as admin, showing cross-user visibility), and the
unsupported-preview fallback page. Delete actions specifically are verified by the
second Playwright test above (and by the `curl` smoke tests), not by the
`claude-in-chrome` session — its CDP connection blocks on a native `confirm()` dialog,
which Playwright handles via its `dialog` event instead.

## 100 MB upload verification

`scripts/verify-100mb-upload.sh` generates a 100 MB file programmatically (never
committed to the repo), uploads it to a running API instance, confirms the
server-reported size and SHA-256 checksum match the local file, downloads it back, and
confirms the downloaded bytes are byte-for-byte identical — then cleans up
(soft + hard delete) after itself.

```bash
API_BASE_URL=http://localhost:5028 ./scripts/verify-100mb-upload.sh
```

**Actual run performed during development** (API running locally against LocalDB,
manual `curl` steps equivalent to the script, since the script itself hit an
environment-specific background-process issue described below):

- Uploaded a 104,857,600-byte (100 MiB) file. Server responded `201 Created` with
  `sizeBytes: 104857600` and a SHA-256 checksum that matched `sha256sum` of the local
  file exactly.
- Upload completed in **1.46 seconds** over loopback (`time curl …`, from the terminal
  output captured during this session).
- Downloaded the file back; `sha256sum` of the downloaded bytes matched the original
  exactly, and the file size matched exactly (104,857,600 bytes both times).
- Memory behavior: the running `FileStorage.Api` process's working set was sampled via
  `Get-Process` before and immediately after two consecutive 100 MB uploads:
  **90.3 MB → 99.3 MB** (≈9 MB net change across ~200 MB of total transferred file
  content). This is consistent with streaming rather than full in-memory buffering,
  though the honest caveat is that a 100 MB transfer over loopback completes in ~1.5
  seconds, too fast for coarse-grained external process-memory polling to reliably
  catch a transient peak — the **authoritative** evidence for the no-buffering claim is
  the code itself: `FileSystemStorage.SaveNewAsync` allocates exactly one
  `byte[UploadPolicy.CopyBufferSizeBytes]` (80 KB by default) regardless of file size,
  and never constructs a `MemoryStream`, `byte[]` sized to the upload, or base64
  representation of the content — see that method directly for the loop.
- `scripts/verify-100mb-upload.sh` reproduces the same sequence in one command; it was
  authored and is intended to be run directly in a persistent shell (its one execution
  during this session was affected by the harness's background-process handling across
  tool-call boundaries, not by a defect in the script's logic — every step it automates
  was independently verified manually as described above).

## Assumptions

- "Version number (optional)" from the assessment is implemented as a fixed `1` on
  creation with no version-history table — full version history was explicitly marked
  optional and out of scope for the 8–12 hour target.
- `metadata.json` mentioned as *optional* in the assessment's storage layout diagram is
  not written; all metadata lives in SQL Server, which is the source of truth.
- A "directly navigable preview page" needing a supporting metadata endpoint (mentioned
  in PROMPT.md §7) is `GET /api/files/{id}`, documented above as a supporting endpoint,
  not part of the assessment's core four CRUD-ish endpoints.
- All three optional bonus features (resumable uploads, audit log, ETag-based caching)
  were subsequently implemented and tested, in a later review pass that explicitly
  requested them as required deliverables for that pass. See "Bonus features" below.

## Bonus features

### Resumable uploads

Protocol (`POST/GET/PUT/POST/DELETE /api/upload-sessions...`, full route table above):

1. `POST /api/upload-sessions` with `{ fileName, contentType, totalSizeBytes, tags }`
   creates a session (`UploadSessions` table row: owner, declared total size, received
   bytes, status, expiry) and returns `nextExpectedOffset: 0`.
2. `PUT /api/upload-sessions/{id}/chunks?offset={n}` with the raw chunk bytes as the
   request body appends at exactly `offset`, which must equal the session's current
   `nextExpectedOffset` (its `ReceivedBytes`). A chunk is streamed straight to a
   per-session temp file (`_storage*/​_uploadsessions/{id}.part`) in fixed 80 KB reads —
   never buffered whole in memory, regardless of chunk or file size. Concurrent chunk
   requests for the *same* session are serialized by an in-process lock (documented
   limitation: this is per-instance, not distributed — see below).
3. `GET /api/upload-sessions/{id}` returns current status, for a client to resume after
   a dropped connection or a page reload: re-fetch `nextExpectedOffset`, then continue
   sending chunks from there.
4. `POST /api/upload-sessions/{id}/finalize`, once `ReceivedBytes == TotalSizeBytes`,
   streams the assembled temp file to compute its SHA-256, then commits it through the
   exact same atomic temp-then-rename path a normal upload uses, and creates the
   `StoredObjects` row. A second `finalize` call `409`s (the session is no longer
   `InProgress`) — it cannot create a duplicate file.
5. `DELETE /api/upload-sessions/{id}` aborts and discards the partial temp file.

Ordering/edge cases: a chunk at the wrong offset `409`s with the correct
`nextExpectedOffset` in the message; a chunk that exactly replays bytes already durable
(offset less than the current length) is accepted idempotently without re-writing,
so a client that resends a chunk after not seeing its response doesn't corrupt state.
Session state lives in SQL Server, not process memory, so it — and the partial file on
disk — survive a backend restart; a client resumes against the new process exactly as
it would resume after any other interruption. Only the session's owner can append
chunks, finalize, or abort (admin included, this app's admin bypass does not extend to
someone else's in-progress upload); `GetStatus` does allow admin read access,
consistent with admin visibility elsewhere in the app. Expired (`TTL` = 24h by default,
`ResumableUpload:SessionTimeToLiveHours`) `InProgress` sessions are swept by
`ResumableUploadService.CleanupExpiredSessionsAsync`, which discards only their own
partial temp file and never touches a session still within its TTL; this is exposed as
a method for now (call it from an admin/maintenance path or a scheduled job), not wired
into an automatic background `IHostedService` — see limitations.

Frontend: `frontend/src/app/storage/services/resumable-upload.service.ts` +
`upload.component.ts`. Files at or above 20 MiB automatically use the chunked protocol
(2 MiB chunks; visible "Resumable" badge); smaller files keep using the simpler one-shot
multipart POST, which has less overhead for small files. Retrying a failed resumable
upload re-queries the session's actual server-side progress and continues from there —
it does not restart from byte 0, and the UI says so explicitly.

Verified: `tests/FileStorage.IntegrationTests/ResumableUploadTests.cs` (in-order
chunking → finalize with matching checksum and byte-for-byte downloaded content; wrong
offset → 409; idempotent duplicate-chunk replay; finalize-before-complete → 409;
double-finalize does not duplicate the file; cross-user session access denied; abort
then reject further chunks/finalize; **and a genuine restart-recovery test** that
uploads half a file against one `WebApplicationFactory`/host instance, disposes it, then
resumes against a *second*, independent instance pointed at the same storage
root/database — proving durability across a real process boundary, not just in-memory
state). `tests/FileStorage.UnitTests/Application/ResumableUploadServiceTests.cs` covers
expiry cleanup (only past-TTL sessions touched) and a bounded 8-way concurrent-chunk-
append test proving the per-session lock serializes writes without corrupting or losing
bytes. Also verified through the real browser UI (see the latest QA report under
`Reports and evidence` for exact evidence paths): a 25 MiB file generated client-side,
uploaded via the real Angular upload component, shown with the "Resumable" badge, and
its post-finalize server-reported checksum confirmed to exactly match a fresh SHA-256
of the downloaded bytes.

Known limitations: the per-session chunk-append lock is in-process only (a
multi-instance/load-balanced deployment would need a distributed lock instead — e.g. a
SQL Server application lock — to prevent two instances from both accepting a chunk for
the same session); expiry cleanup is on-demand (a method to call), not a scheduled
background job, mirroring the existing `--reconcile` command's same on-demand design
philosophy elsewhere in this codebase.

### Audit log

Persists one row per meaningful operation (`AuditLogEntries` table): actor user id,
actor role, operation name (`FileUpload`, `FileSoftDelete`, `FileHardDelete`,
`ResumableUploadFinalize`), resource id/type, outcome, an optional short detail string
(never file contents, tokens, or secrets), the request's correlation id, and a UTC
timestamp. `ResourceId` is a **plain string column with no foreign key** to
`StoredObjects` on purpose: an audit entry must remain readable after the file it
describes has been hard-deleted, and a real FK (even with `ON DELETE SET NULL`) would
either block the delete or destroy the resource-id trail. Writes are **best-effort** —
`AuditLogWriter.RecordAsync` catches and logs any persistence failure instead of
throwing, so a transient audit-table problem can never fail or roll back the file
operation it's describing; this is a deliberate choice (a compliance/visibility aid
should not become an availability risk for the core product), not an oversight.

Read access: `GET /api/admin/audit-log` (paginated, filterable by actor/operation/
resource id/date range), `[Authorize(Roles = Admin)]` — a normal user gets `403`.
There is no write or delete endpoint at all for audit data through the API, by any
role — the only way entries are created is internally, by the operations that emit
them.

Verified: `tests/FileStorage.IntegrationTests/AuditLogTests.cs` — an upload produces a
`FileUpload` entry whose `CorrelationId` matches that exact request's own response
header; soft-delete then hard-delete of the same file produce `FileSoftDelete` and
`FileHardDelete` entries that remain queryable by `resourceId` **after** the file row
is physically gone; a non-admin gets `403` reading the log and has no route available to
alter it; actor/operation filtering returns only matching rows.

Known limitation: no cryptographic tamper-evidence (hash chaining, signing) is
implemented or claimed — an administrator with direct database access could edit audit
rows. This is a plain audit trail for operational visibility, not a tamper-proof ledger.

### ETag-based caching

`GET`/`HEAD /api/files/{id}/download` and `GET /api/files/{id}/preview` return a
**strong** `ETag` — the file's own SHA-256 checksum, quoted — rather than a weak,
timestamp-derived one, because the checksum is exactly the representation identity
that changes if and only if the bytes change (which, for this app's immutable-once-
uploaded content model, is never after creation). `Last-Modified` mirrors
`CreatedAtUtc`. `Cache-Control: private, no-cache` is set explicitly so a shared/proxy
cache can never store or replay these per-user, authenticated responses across users,
while still allowing the browser's own conditional-revalidation flow (hence
`no-cache`, not `no-store`: the browser is *allowed* to keep a copy but must always
revalidate with `If-None-Match` before reusing it). Conditional-request evaluation
(`If-None-Match` → `304` with an empty body, `If-Range` interaction with `Range`) is
ASP.NET Core's own built-in `PhysicalFileResult` behavior, driven by the `EntityTag`/
`LastModified` properties this app sets — not custom-written 304 logic.

Authorization is always resolved **before** a `PhysicalFileResult` (and therefore
before any conditional-request short-circuit) is even constructed: `FileAccessService`
throws `404` for a nonexistent, someone-else's, or already-inaccessible file before the
controller action ever reaches the point of setting an `ETag`. A stale `If-None-Match`
value for a file that has since been hard-deleted, or that never belonged to the
caller, still correctly `404`s — it can never `304`.

Verified: `tests/FileStorage.IntegrationTests/ETagCachingTests.cs` — normal response
carries a stable `ETag` and `private` `Cache-Control`; matching `If-None-Match` → `304`
with an empty body (both `GET` and `HEAD`); non-matching → full `200` content; matching
`If-Range` → `206` partial content with correct bytes; stale `If-Range` → full `200`
(Range ignored, per RFC 7233); preview also carries an `ETag`; authorization is checked
before any conditional logic runs (a nonexistent id with a syntactically valid
`If-None-Match` still `404`s, never `304`s); a hard-deleted file's previously-valid
`ETag` also still `404`s, never `304`s.

## Known limitations

- **Docker remains an outstanding verification item.** Rechecked 2026-09-17: the Docker
  CLI is present (`docker --version` → `Docker version 29.8.0`) and free disk space is
  adequate (6.4 GB free on the build drive) — both earlier blockers ("Docker not
  installed", "0.2 GB free") no longer apply and should not be repeated. The actual
  current blocker is different: the Docker daemon/engine is not reachable from this
  shell (`docker images` → `request returned 500 Internal Server Error ...
  dockerDesktopLinuxEngine/_ping`), so no image can be built or container started here.
  `docker compose config` does parse `docker-compose.yml` without error. Neither
  starting the Docker Desktop engine nor further environment changes were attempted, as
  that's outside the scope of a documentation pass. The Dockerfiles and
  `docker-compose.yml` were written carefully
  (multi-stage builds, non-root API container user, health-check-gated startup
  ordering, named volumes for both SQL Server data and uploaded files, an nginx reverse
  proxy configured for `proxy_request_buffering off` so large uploads actually stream
  through it) and reviewed line-by-line, but build/startup/SQL-Server-readiness/
  migrations/auth/upload-download/persistence-after-recreate were not literally
  executed. None of that verification depends on Windows LocalDB — the compose file's
  `sqlserver` service is the Linux `mcr.microsoft.com/mssql/server:2022-latest`
  container image, entirely separate from LocalDB (which this repo only uses for local,
  non-Docker development and the integration test suite). A reviewer with Docker
  should run:
  ```bash
  docker compose up --build -d
  docker compose ps                                    # all three services healthy
  TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/mock-token -H "Content-Type: application/json" -d '{"role":"user"}' | jq -r .accessToken)
  curl -X POST http://localhost:5080/api/files -H "Authorization: Bearer $TOKEN" -F "file=@somefile.txt"
  docker compose restart api
  curl http://localhost:5080/api/files -H "Authorization: Bearer $TOKEN"   # file still listed
  ```
- **The DB/filesystem crash-window gap** described in `docs/architecture-decisions.md`
  §5 now has a recovery mechanism — the `--reconcile` administrative command (see
  "Database/filesystem reconciliation" above) — but it is deliberately on-demand, not a
  scheduled background job. An inconsistency from a crash at exactly the wrong moment
  will persist undetected until someone runs `--reconcile`; wiring it into a cron job
  or scheduled task is an operational step outside this codebase.
- **Resumable uploads: known, unfixed concurrency and validation gaps**, confirmed by
  reading the current implementation (`ResumableUploadService`,
  `UploadSessionsController`, `FileSystemStorage`) on 2026-09-17. These are documented
  here as-is, not fixed, per this pass's scope (documentation only):
  1. **No lock coordination between `FinalizeAsync`/`AbortAsync` and
     `AppendChunkAsync`.** Only `FileSystemStorage.AppendChunkAsync` takes the
     per-session semaphore; `FinalizeAsync` (which reads the assembled temp file to
     hash and commit it) and `AbortAsync` (which discards it) do not acquire that same
     lock. A chunk append racing a concurrent finalize/abort on the same session can
     read a partially-written or already-deleted temp file.
  2. **A rejected over-budget chunk can leave stray bytes that a retry silently
     accepts.** `FileSystemStorage.AppendChunkAsync` writes each read buffer to disk
     before the next iteration's over-budget check throws; the already-written bytes
     from that rejected chunk are not rolled back, and `ReceivedBytes` is never
     persisted for the failed call. A client retry at its last-known offset then hits
     the idempotent-replay branch (`expectedOffset < currentLength` → return
     `currentLength`), which reports success without re-validating those stray bytes
     against any limit.
  3. **The chunk endpoint's `[RequestSizeLimit]` is a hardcoded 32 MiB constant**
     (`UploadSessionsController.MultipartHeaderLimits.MaxChunkRequestBodyBytes`),
     independent of the configurable `ResumableUpload:MaxChunkSizeBytes` option
     (default 8 MiB). Raising or lowering the configured chunk size does not change
     this ASP.NET Core-level cap.
  4. **The per-chunk size check is bypassed for chunked-transfer-encoding requests.**
     `UploadSessionsController.AppendChunk` derives the declared chunk length from
     `Request.ContentLength ?? 0`; when `Content-Length` is absent (chunked
     transfer-encoding), the declared length is `0`, which is never greater than
     `MaxChunkSizeBytes`, so `ResumableUploadService.AppendChunkAsync`'s per-chunk
     limit check is a no-op for such requests. Only the cumulative session-total cap
     in `FileSystemStorage.AppendChunkAsync` still applies.
  5. **The per-session lock dictionary is never cleaned up.**
     `FileSystemStorage.UploadSessionLocks` is a `static ConcurrentDictionary<string,
     SemaphoreSlim>` keyed by temp-file path; entries are added on first chunk append
     and never removed (not on finalize, abort, or expiry), so the dictionary grows
     unboundedly under sustained resumable-upload traffic — a long-lived in-process
     leak.
  6. **Admin bypass is inconsistent with the rest of the app.** `GetStatusAsync` allows
     an admin to view any user's session (`isAdmin` bypass), but `AppendChunkAsync`,
     `FinalizeAsync`, and `AbortAsync` all use `IsOwnedBy` checks with no admin bypass —
     an admin cannot append to, finalize, or abort another user's in-progress session,
     unlike the admin-can-access-everything pattern elsewhere (e.g. `FileAccessService`
     for ordinary downloads).
  7. **`FinalizeAsync` re-hashes the whole assembled file** via
     `FileSystemStorage.ComputeChecksumAsync`, a full second read pass over the
     temp file, rather than maintaining an incremental SHA-256 across the
     `AppendChunkAsync` writes — extra I/O proportional to file size on every finalize.
  8. **`CommitWithCollisionRetryAsync` is duplicated** — the same 5-attempt
     storage-key-collision-retry loop is implemented separately (not shared) in both
     `UploadFileService` and `ResumableUploadService`.

  None of these affect the one-shot upload path (files below the 20 MiB resumable
  threshold), and the per-session chunk-append lock that does exist is per-instance,
  not distributed — a multi-instance/load-balanced deployment would need a distributed
  lock (e.g. a SQL Server application lock) instead. The original one-shot upload path
  also has no idempotency-key protocol of its own — a network failure mid-upload there
  still requires a full re-upload, and the UI documents the resulting
  duplicate-on-retry risk rather than hiding it.
- **Test database cleanup on LocalDB is best-effort.** Each integration test method
  creates its own uniquely-named LocalDB database for full isolation, and its `Dispose`
  attempts `EnsureDeleted()` after clearing SqlClient's connection pool — but on
  Windows/LocalDB this occasionally no-ops due to a still-attached pooled connection.
  Leftover `FileStorageIntegrationTests_*` databases are inert and harmless; run
  `scripts/cleanup-test-dbs.sql` periodically to reclaim them.
- **Angular strict mode + Node version.** The installed Node.js (v24) is newer than
  Angular 17's officially supported range, which the Angular CLI reports as a warning
  on every command; both `ng build` and `ng test` completed successfully despite the
  warning.

## Future enhancements

- Scheduling `--reconcile` (a cron entry, a scheduled task, or a lightweight
  `BackgroundService` that shells out to it or calls `ReconciliationService` directly
  on a timer) so the crash-window gap in §5 of `docs/architecture-decisions.md` is
  detected proactively rather than only when an operator remembers to run it.
- Optionally alerting/paging when `--reconcile` finds missing-content rows (exit code
  `3`), since those specifically require a human recovery decision.
- A normalized tag table if arbitrary multi-tag boolean queries (AND/OR across tags)
  become a real requirement — the current boxed-delimiter column intentionally doesn't
  support that.
- For resumable uploads specifically (see [Known limitations](#known-limitations) for
  the full list found on review): a distributed session lock for multi-instance
  deployments; closing the finalize/abort-vs-append-chunk race with a shared lock
  scope; deriving the `[RequestSizeLimit]` from `ResumableUpload:MaxChunkSizeBytes`
  instead of a hardcoded constant; enforcing the per-chunk limit against actual bytes
  read rather than the declared `Content-Length`; evicting entries from the
  per-session lock dictionary on finalize/abort/expiry; extending the admin bypass to
  append/finalize/abort; incremental hashing during chunk writes instead of a second
  full-file read at finalize; and extracting the duplicated collision-retry logic into
  a shared helper.
- (Resumable uploads, audit log, and ETag-based caching themselves are already
  implemented — see "Bonus features" above, not a future item.)
