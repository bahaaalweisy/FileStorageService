# Assessment Requirements Checklist

Status against the attached `FullStack_Developer_Assessment.pdf` and `PROMPT.md`. ✅ =
implemented and verified (see README "Tests & verification status" for how); ⚠️ =
implemented but not execution-verified; — = explicitly out of scope per assessment
(bonus/optional).

## Backend

- ✅ Clean Architecture (Api → Application → Domain ← Infrastructure), correct
  dependency direction
- ✅ SQL Server + EF Core 8 code-first migrations (committed, `EnsureCreated` not used)
- ✅ Local filesystem storage only
- ✅ Streaming uploads — `MultipartReader`, no `IFormFile`, no full-buffer reads
- ✅ Streaming downloads with HTTP Range support (200/206/416, framework-provided)
- ✅ JWT auth, roles `user`/`admin`, signature/issuer/audience/expiration validated
- ✅ Metadata: original name, key, size, content type, SHA-256 checksum, tags,
  created/deleted timestamps, version, `CreatedByUserId`
- ✅ Soft delete + hard delete (hard delete requires prior soft delete; admin-only)
- ✅ Structured logging + correlation ID (accepted or generated, in logs + response
  header)
- ✅ Configuration-based upload constraints (size, extensions, filename/tag limits)
- ✅ RFC 7807 ProblemDetails error handling, centralized
- ✅ Health checks: `/health/live` (no DB dependency), `/health/ready` (DB + real
  filesystem read/write probe)

## Frontend (Angular 17, standalone components)

- ✅ Demo login generating a mock JWT via a backend endpoint
- ✅ Upload UI: drag & drop, file picker, real `HttpClient` upload-progress events,
  manual retry, duplicate-submission guard
- ✅ File listing: pagination, filters (name/tag/content-type/date range), loading/
  empty/error states, debounced text filtering, cancellation of obsolete requests via
  `switchMap`
- ✅ File preview: routable `/files/:id` (works after refresh), authenticated
  image/PDF preview via blob object URLs (not raw `<img src="/api/...">`), metadata,
  download action, unsupported-type fallback
- ✅ Soft delete (any authorized user) and hard delete (admin-only) actions, with
  confirmation before both
- ✅ Admin-only deleted-files listing (`GET /api/files/deleted`), server-backed —
  survives refresh/new sessions, rejects non-admins with 403; makes the two-step
  hard-delete policy actually usable
- ✅ Toast notifications
- ✅ `core/` / `shared/` / `storage/` structure
- ✅ Committed Playwright E2E tests: login → upload → list → download (byte-verified),
  and admin soft-delete → find → hard-delete (verifying the two-step policy is
  actually usable through the UI, not just the API)

## Non-functional

- ✅ Filename sanitization (never used for path construction)
- ✅ Directory traversal prevention (opaque server-generated keys + path containment
  check)
- ✅ Atomic file writes (temp file → atomic rename)
- ✅ 100 MB streaming upload verified (see README) — decimal-byte limit, documented
- ✅ Configuration-driven max upload size, enforced during streaming independent of
  `Content-Length`
- ✅ Dependency injection lifetimes correct (DbContext scoped, `IFileStorage`
  singleton and genuinely thread-safe, no scoped-into-singleton violations)
- ✅ Documented assumptions and decisions (`docs/architecture-decisions.md`)
- ✅ DB/filesystem reconciliation: `--reconcile` admin command, report-only by
  default, detects orphan files and missing content, never mutates the database.
  Destructive cleanup (`--delete-orphans`) requires exclusive access to the storage
  root, enforced by a real OS-level file lock — not merely an age heuristic; verified
  by staging an orphan, confirming deletion is refused (exit `4`) while the API holds
  the lock, then succeeds once it's released (README "Database/filesystem
  reconciliation")

## Docker & delivery

- ✅ Dockerfiles (API multi-stage, frontend multi-stage + nginx), `docker-compose.yml`,
  `.env.example`
- ⚠️ Docker Compose startup — written and reviewed, **not execution-verified**
  (rechecked 2026-09-17: the Docker CLI is now present, but the daemon/engine is not
  reachable in this environment; see README "Known limitations")
- ✅ README with setup, architecture, decisions, limitations, future enhancements
- ✅ This requirements checklist

## Bonus (implemented; test suite re-run and confirmed 2026-09-17)

- ✅ Resumable uploads — `POST /api/upload-sessions` + chunk append with idempotent
  duplicate/retried-chunk handling (`FileSystemStorage.AppendChunkAsync`); session
  creation confirmed returning `201` with a real session id. **Known, unfixed
  concurrency/validation gaps found on code review** — no lock coordination between
  finalize/abort and chunk-append, a rejected over-budget chunk can leave stray bytes a
  retry silently accepts, the chunk endpoint's `[RequestSizeLimit]` is a hardcoded
  32 MiB independent of the configurable chunk-size option, the per-chunk limit is
  bypassed for chunked-transfer-encoding requests, the per-session lock dictionary is
  never cleaned up, admin bypass doesn't extend to append/finalize/abort, finalize
  re-hashes the whole file instead of hashing incrementally, and the collision-retry
  logic is duplicated with `UploadFileService`. See README "Known limitations" and
  `docs/architecture-decisions.md` §14 for the full list — not fixed in this pass.
- ✅ Audit log — `GET /api/admin/audit-log`; confirmed returning real historical
  entries (upload/soft-delete/hard-delete/resumable-finalize) with actor, role,
  outcome, correlation id, timestamp
- ✅ ETag-based caching — downloads/previews set `ETag` (from the stored SHA-256) and
  `Last-Modified`, `Cache-Control: private, no-cache`
- ✅ Automated test coverage for these three **re-run and confirmed 2026-09-17**:
  `dotnet test tests/FileStorage.UnitTests` (61/61 passed, includes
  `ResumableUploadServiceTests`, `AuditLogWriterTests`) and
  `dotnet test tests/FileStorage.IntegrationTests` against real SQL Server LocalDB
  (45/45 passed, includes `ResumableUploadTests`, `AuditLogTests`,
  `ETagCachingTests`). This supersedes the earlier note in this file that the
  integration suite "could not run" — it ran, today, and passed. E2E (Playwright)
  coverage for these three was not re-run in this pass (no fresh execution evidence
  exists for 2026-09-17); see README "Tests & verification status".
