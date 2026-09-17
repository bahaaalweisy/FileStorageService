# Architecture Decisions

This file records the decisions and assumptions made while implementing the File
Storage Service assessment, distinguishing explicit assessment requirements from
implementation choices made to resolve ambiguity. It substitutes for an interactive
`/office-hours` session (see `docs/tooling-report.md` for why that wasn't run as a
separate step) — these are the same categories of decisions that process would have
produced, reasoned through directly against the assessment PDF and PROMPT.md.

## 1. Clean Architecture boundaries

- **Domain** (`FileStorage.Domain`): `StoredObject` entity, `TagSet`/`StorageKey` value
  objects, `UserRole` constants. No dependency on any other project.
- **Application** (`FileStorage.Application`): use-case services
  (`UploadFileService`, `ListFilesService`, `FileAccessService`, `DeleteFileService`),
  abstractions (`IFileStorage`, `IStoredObjectRepository`, `ICurrentUser`, `IClock`),
  DTOs, `UploadPolicyOptions`, and `FilenameSanitizer`. Depends only on Domain.
- **Infrastructure** (`FileStorage.Infrastructure`): EF Core `AppDbContext` +
  migrations, `StoredObjectRepository`, `FileSystemStorage`, `StoragePathResolver`.
  Depends on Application + Domain, implements Application's ports.
- **Api** (`FileStorage.Api`): controllers, the upload Minimal API endpoint, JWT
  auth, ProblemDetails middleware, health checks, `Program.cs` composition root.
  References Application directly and Infrastructure only for DI registration.

No abstraction layer wraps EF Core or the filesystem beyond what's needed to satisfy
the Application-layer ports — there is no generic repository, no MediatR, no
AutoMapper. Manual DTO mapping (`FileResponseMapper`, `UploadFileService.Map`) was
judged simpler and more debuggable than a mapping library for ~10 fields.

## 2. Upload is a Minimal API endpoint, not an MVC controller action

`POST /api/files` is implemented in `src/FileStorage.Api/Files/FileUploadEndpoint.cs`
as a Minimal API (`app.MapPost`), not as a `FilesController` action, even though every
other file endpoint is a controller. This section previously claimed ASP.NET Core MVC
was "fundamentally incompatible" with manual multipart streaming — **that claim was
wrong and has been corrected below.** MVC controllers can stream a multipart body with
`MultipartReader` exactly as Microsoft's official "large file uploads" sample
documents; the Minimal API here is a design preference, not a workaround for a
framework limitation. Microsoft's current sample already removes all three form
value-provider factories discussed below — an earlier version of this document
incorrectly implied the sample omits one of them. That was a mistake in what this
project initially implemented (adapted from an older/incomplete two-factory pattern
seen elsewhere), not a gap in Microsoft's documentation.

**Observed root cause, in this implementation.** On the .NET 8 SDK/runtime this
project was built and tested against (SDK 10.0.301, ASP.NET Core runtime 8.0.28), with
this project's specific route and filter configuration, `ControllerActionInvoker` was
observed to build a `CompositeValueProvider` while dispatching to a controller action —
including a bare action with no bound parameters — from the registered
`IValueProviderFactory` list. Three factories in that list access `Request.Form`
(directly or via `ReadFormAsync()`) whenever the request has form content type:
`FormValueProviderFactory`, `JQueryFormValueProviderFactory`, and
`FormFileValueProviderFactory`. This is reported as what was observed in this specific
environment and configuration, not as a universal claim about every MVC action in
every ASP.NET Core version or hosting configuration.

Removing only the first two factories (a resource filter matching some
widely-copied, older versions of this pattern) was **not** sufficient in this
implementation: `FormFileValueProviderFactory` still touched `Request.Form` and still
drained the body before the action ran, reproducing the same `IOException: Unexpected
end of Stream`. Removing all three — matching Microsoft's current official sample —
fixed it. This was root-caused, not assumed, by instrumenting the resource filter
itself: logging `ValueProviderFactories` before and after `RemoveType<T>()` calls
confirmed which factories were actually being removed at each step, and a
`Request.Body.ReadAsync()` probe as the first line of the action returned `0` bytes
with only two factories removed, and the correct byte count once all three were
removed — confirming a controller action **can** stream a multipart body correctly in
this implementation once all three factories are removed.

**Decision.** Given a controller-based fix is confirmed to work here, the choice to
implement the upload endpoint as a Minimal API is a simplicity preference, not a
requirement: it needs no custom `IResourceFilter` class, no `[DisableRequestSizeLimit]`
/ `[RequestFormLimits]` / `[DisableFormValueModelBinding]` attribute stack, and the
body-reading logic is identical either way. All other endpoints remain ordinary MVC
controller actions since none of them read the request body manually, so there's no
consistency benefit to converting this one that would offset the churn of doing so.

For an equivalent controller-based implementation, matching Microsoft's current
official sample, apply a resource filter like:

```csharp
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
```

## 3. Storage key vs. row Id, and path layout

`StoredObject.Id` (GUID, primary key) and `StoredObject.Key` (opaque, server-generated,
22-char base64url string — `StorageKey.Generate()`) are deliberately separate. The
physical path is derived only from `Key` and `CreatedAtUtc`:

```
_storage/yyyy/MM/dd/{key}/content.bin
```

Using a random key (not the GUID Id, and never the client filename) means the
directory name carries no information correlated with database identifiers, and the
per-key directory guarantees no collision between two files uploaded the same day —
even if a future change made `Key` less random, the directory still isolates content.
`StoragePathResolver` validates the key format (`^[A-Za-z0-9_-]{8,64}$`) and verifies
the resolved absolute path is still contained under the storage root before returning
it, as defense in depth even though the key generator can't produce path separators.

## 4. Tag storage: boxed-delimiter string, not a join table

Tags are stored as a single `nvarchar(1024)` column formatted as
`|tag1|tag2|tag3|` (see `TagSet`). A tag-filter query becomes
`Tags LIKE '%|needle|%'` with the needle pre-boxed in delimiters. This was chosen over
a normalized `Tag`/`StoredObjectTag` join table because:

- The assessment's scope is a single `StoredObjects` table with a `Tags` field, not a
  tagging subsystem.
- The boxed-delimiter format still gets the one correctness property that matters
  (no accidental substring matches — filtering `tag=cat` cannot match a stored tag
  `category`), which a naive `Tags LIKE '%cat%'` would get wrong.
- A join table would add a second index maintenance path and a join to the hot listing
  query for a benefit (arbitrary multi-tag boolean queries) the assessment doesn't ask
  for.

Tags are normalized (trimmed, lower-cased, delimiter-stripped, length- and
count-bounded) and deduplicated in `TagSet.FromInput`.

## 5. Database/filesystem consistency strategy

SQL Server and the local filesystem do not share a transaction — this implementation
does not claim otherwise. The actual sequencing in `UploadFileService.UploadAsync` and
`FileSystemStorage.SaveNewAsync`:

1. Content streams to a uniquely-named temp file under `_storage/_tmp/`, hashed
   incrementally, size-capped while writing.
2. The temp file is closed, then atomically renamed (`File.Move`, same volume) to its
   final path. A partially-written file is never visible at the final path.
3. Only after the rename succeeds does the application attempt to persist metadata to
   SQL Server.
4. **If step 3 fails** (`UploadFileService.UploadAsync`'s catch block): the just-written
   file is deleted as compensation, and the exception propagates as a 500. This handles
   the ordinary "DB unreachable/constraint violation right after a successful write"
   case immediately, synchronously, within the same request.
5. **If the process crashes between step 2 and step 3** (or between the compensating
   delete and its completion): the compensation never runs. The result is an orphaned
   file on disk with no corresponding database row — or, symmetrically, a database row
   whose content file went missing (e.g. a crash mid-rename, or manual/external
   interference with the storage volume). This crash window is real and is not
   prevented — a shared transaction across SQL Server and the filesystem doesn't exist.
   What this implementation **does** provide is recovery: `--reconcile`, an offline
   administrative command (`FileStorage.Application.Maintenance.ReconciliationService`,
   invoked via `dotnet FileStorage.Api.dll --reconcile`), walks the storage tree once,
   streams every `StoredObjects` row's key, and reports both directions of
   inconsistency — orphan files and missing content — without modifying anything by
   default. A `--grace-minutes` window (default 15) excludes recently-written files
   from the *report* so a file mid-upload (already renamed into place, database write
   not yet committed) is unlikely to be flagged as a false positive — this is a
   reporting heuristic based on file age, not a safety guarantee (age alone cannot
   prove a file is inactive). An explicit `--delete-orphans` flag will remove confirmed
   orphan files, but only after acquiring an OS-level exclusive lock on the storage
   root (`FileStorage.Infrastructure.Storage.MaintenanceLock`) that the API process
   holds for its entire serving lifetime — if the API (or another reconcile process) is
   running, `--delete-orphans` is refused outright (exit code `4`), regardless of file
   age. That lock, not the grace window, is the actual safety mechanism for the
   destructive action; see §13 below for its scope and limits. Missing-content rows are
   never auto-fixed since recovering them is a human decision (restore from backup, or
   soft/hard-delete the row via the API once confirmed unrecoverable) — see the README
   "Database/filesystem reconciliation" section for the full recovery procedure and a
   verified example run. There is no *scheduled* background job — the command is
   designed to be run periodically by an operator or a cron/scheduled-task entry, which
   is itself a known limitation (see README).
6. **Request cancellation** during the write loop (`FileSystemStorage.SaveNewAsync`)
   is caught the same way as any other exception: the temp file is deleted, nothing is
   persisted, and no final file is ever created.
7. **Filesystem rename failure** propagates as an exception from `SaveNewAsync`, which
   triggers the same temp-file cleanup; no database row is ever created for a file that
   never successfully committed to its final path.

Hard delete removes the physical file **before** the database row (`DeleteFileService.
HardDeleteAsync`), so a failure between those two steps leaves a database row pointing
at now-missing content rather than a file with no row — the former is detectable (the
row still exists and can be retried), the latter would be silently invisible. If the
physical file is already missing when hard delete runs, that's treated as a successful
deletion outcome (idempotent), but a genuine I/O error (permissions, lock) is not
swallowed — it propagates as a 500 and the database row is left intact for retry.

## 6. Authorization: 404 (not 403) for cross-user access

A non-admin user requesting another user's file — via download, preview, metadata, or
soft delete — receives **404 Not Found**, identical to requesting a file that doesn't
exist at all. This is deliberate: it avoids confirming to an unauthenticated-feeling
caller (from the resource's perspective) that a given file ID belongs to *someone*,
even if not to them. `FileAccessService` and `DeleteFileService.SoftDeleteAsync`
implement this uniformly by querying for the file and then checking
`StoredObject.CanBeAccessedBy(userId, isAdmin)` before returning it; a failed check and
a genuine absence both raise `NotFoundAppException`.

The one deliberate exception is `DELETE /api/files/{id}/hard` for a non-admin caller:
this returns **403 Forbidden**, because the hard-delete capability itself (not the
existence of any particular file) is the thing being gated, and there's no privacy
benefit to hiding that a role lacks a capability.

## 7. Hard delete requires a prior soft delete

`StoredObject.CanBeHardDeleted` is `true` only once `DeletedAtUtc` is set.
`DeleteFileService.HardDeleteAsync` enforces this as a 409 Conflict for an admin who
tries to hard-delete a file that hasn't been soft-deleted yet. This gives admins an
explicit two-step confirmation path for an irreversible action, and the same rule is
surfaced in the Angular UI: `FileListComponent` only renders a "Hard delete" button
once a row is shown as soft-deleted client-side (see decision 9 below — soft-deleted
files don't appear in the list endpoint at all, so the UI keeps the just-deleted row
visible locally to make the two-step flow demonstrable in one session).

## 8. Mock JWT login

The assessment describes "a login screen generating a mock JWT" with "no real auth
backend needed." This implementation interprets that as: a small, explicitly
dev/demo-gated backend endpoint (`POST /api/auth/mock-token`, gated behind
`Jwt:EnableMockTokenEndpoint`) that signs a real, correctly-signed JWT for one of two
**fixed** demo identities (a stable GUID per role, chosen so uploads by "the demo user"
stay attributed to the same `CreatedByUserId` across restarts). The signing key lives
only in backend configuration (`Jwt:SigningKey`, provided via environment variable in
Docker) and is never sent to or duplicated in the Angular app. The Angular login screen
only ever calls this endpoint and stores the returned token — it does not construct or
sign anything itself. Outside the demo/assessment environment,
`Jwt:EnableMockTokenEndpoint` should be left at its `appsettings.json` default of
`false`, at which point the whole `AuthController` 404s and there is no way to obtain a
token — appropriately, since there's no real credential-based login to replace it in
this codebase.

## 9. List excludes soft-deleted; a dedicated, server-backed admin deleted-files view

Per the assessment's stated policy, soft-deleted files never appear in `GET
/api/files`. That collides with also wanting a usable "hard delete" flow, since an
admin can't hard-delete something they can no longer see in the normal list — and a
soft delete needs to remain reachable across page refreshes and separate sessions, not
just within the browser tab that performed it.

**Earlier version of this decision (superseded):** the first implementation kept
soft-deleted rows visible only in client-side component state after a soft delete
succeeded. That was wrong — reloading the page, or an admin starting a fresh session,
lost all access to the file, making the two-step hard-delete policy unusable outside a
single continuous browser session.

**Current implementation:** a real, admin-only, database-backed endpoint —
`GET /api/files/deleted` (`FilesController.ListDeleted`, `[Authorize(Roles =
"admin")]`) — lists soft-deleted rows directly from `StoredObjects` (`ListFilesQuery.
OnlyDeleted = true`, enforced in `StoredObjectRepository.ListAsync`). The normal `GET
/api/files` endpoint always passes `OnlyDeleted: false` regardless of caller role, so
excluding deleted files from the normal listing is guaranteed independent of who's
asking — it isn't something a client could accidentally or deliberately toggle. The
Angular `AdminDeletedFilesComponent` renders this list (visible only when
`auth.isAdmin()`) with its own hard-delete action; `FileListComponent`'s soft-delete
action now simply removes the row from its own view and shows a toast — it holds no
special "still visible but deleted" state at all. Verified end to end (including an
actual page reload between the soft delete and locating the file again) by
`AuthorizationAndDeleteTests.DeletedFilesListing_SurvivesReload_AndAllowsHardDeleteAfterward`
and the Playwright test `admin-delete-lifecycle.spec.ts`; non-admin access is rejected
both by `DeletedFilesListing_IsForbiddenToNonAdmin` (integration) and by a Playwright
test that calls the endpoint directly.

## 10. Upload size units and default limit

`UploadPolicy:MaxFileSizeBytes` is expressed in **decimal bytes**, defaulting to
209,715,200 — which is 200 MiB (200 × 1024 × 1024), not 200,000,000 decimal-MB. This
default was chosen so the required 100 MB demonstration upload has 2× headroom under
the limit, with the exact byte count still fully configurable.

## 11. Multipart field order is not significant (stage/commit split)

`tags` field(s) may appear before or after the file part in the multipart payload —
order does not matter. An earlier version of this implementation required `tags` to
precede the file part, because the file section was written directly to its *final*
location as soon as it was reached, so any tags appearing later in the request were
never collected in time. That constraint is gone: `IFileStorage` now exposes a
two-phase `StageAsync`/`CommitAsync` split (see `FileUploadEndpoint` and
`UploadFileService`). `FileUploadEndpoint` consumes every multipart section in one
pass (required regardless — `MultipartReader` is forward-only): when the file section
is reached, its bytes are streamed to a temp file via `StageAsync` (hashed, size-capped,
same true streaming as before — nothing is buffered in memory), but not committed
anywhere yet. Only after every section has been read — so every `tags` field has been
collected, wherever it appeared — is the upload finalized:
`UploadFileService.FinalizeAsync` commits the temp file into its final path
(`CommitAsync`, an atomic rename) and persists metadata including all collected tags.
If the request is rejected after staging for any reason (a second file part,
cancellation, a downstream validation failure), the staged temp file is discarded
(`DiscardStagedAsync`) rather than left behind. Both field orders are covered by
`UploadDownloadTests.Upload_WithTagsFieldBeforeFilePart_HonorsTags` and
`..._AfterFilePart_HonorsTags`.

## 12. Client-side session storage for the demo JWT

`AuthService` mirrors the token to `sessionStorage` purely so a page refresh during the
demo doesn't force a re-login. This is readable by any script on the page (XSS
exposure) — acceptable here because the tokens are short-lived, scoped to two
non-sensitive fixed demo identities, and issued only by a dev-gated endpoint; it would
not be an acceptable pattern for a real credential-bearing session.

## 13. Reconciliation deletion safety: exclusive-lock scope and limits

`MaintenanceLock` (`FileStorage.Infrastructure.Storage`) is an OS-level exclusive file
lock (`FileShare.None`) over a single well-known file,
`{storageRoot}/_maintenance.lock`. The API process acquires it once, at startup, and
holds it for its entire serving lifetime (released via
`IHostApplicationLifetime.ApplicationStopping`, or simply by OS file-handle cleanup on
process exit). `ReconcileCommand` attempts to acquire the *same* lock before
`--delete-orphans` deletes anything; if acquisition fails, deletion is refused (exit
code `4`) and nothing is touched.

This is a genuine, verifiable exclusivity mechanism — not an operator convention or a
checkbox flag — and was verified by staging an orphan file, running `--reconcile
--delete-orphans` while the API was still running (confirmed refused, orphan
untouched), then re-running the identical command after stopping the API (confirmed it
proceeded and deleted exactly the orphan).

**Explicit scope boundary:** the lock is exclusive with respect to *this
application's own processes* sharing the same storage root — the running API instance,
and any other invocation of `--reconcile --delete-orphans` against that root. It does
**not**, and structurally cannot, prevent:

- An unrelated process (a backup tool, an ad-hoc script, a human with shell/SSH access)
  from reading or writing the storage volume directly, outside this application.
- A second, independently configured deployment of this same application pointed at
  the same storage root but not participating in the same lock file (e.g. a
  misconfigured `Storage:RootPath` pointing at a *different* path that happens to
  resolve to the same physical volume through a symlink or mount).

Treating the lock as "provably no writer anywhere is active" would be an overclaim;
treating it as "no writer *from this application* is active" is the accurate,
verified claim, and is what the README documents.

## 14. Resumable uploads: known, open concurrency and validation gaps

Unlike the other numbered decisions in this file, this section documents **unresolved
issues**, not settled design choices — confirmed by reading the current implementation
(`ResumableUploadService`, `UploadSessionsController`, `FileSystemStorage`) on
2026-09-17, not fixed as part of this documentation pass. They are recorded here so a
reviewer has an accurate picture of the bonus feature's maturity relative to the core
upload/download/delete path, which has been through multiple review-and-fix cycles
(see §5 and §13 above); the resumable-upload path has not yet had an equivalent pass.

1. **No lock coordination between `FinalizeAsync`/`AbortAsync` and
   `AppendChunkAsync`.** The per-session `SemaphoreSlim` in
   `FileSystemStorage.UploadSessionLocks` is acquired only inside `AppendChunkAsync`.
   `ResumableUploadService.FinalizeAsync` (which reads the assembled temp file to hash
   and commit it) and `AbortAsync` (which deletes it) never take that lock, so a chunk
   append racing a concurrent finalize or abort on the same session can observe a
   partially-written or already-deleted temp file.
2. **A rejected over-budget chunk can leave stray bytes on disk that a retry silently
   accepts.** In `FileSystemStorage.AppendChunkAsync`, each buffer read is written to
   the temp file before the next iteration's cumulative-size check throws
   `PayloadTooLargeAppException`; the bytes already written for the rejected chunk are
   not rolled back, and the caller's `ReceivedBytes` is never persisted for a failed
   call (the exception propagates before `session.RecordChunkAppended` runs). A client
   retry at its last-known offset then lands in the idempotent-replay branch
   (`expectedOffset < currentLength` → return `currentLength` without writing),
   which reports success without re-validating those stray bytes.
3. **The chunk endpoint's `[RequestSizeLimit]` is a hardcoded constant, not derived
   from configuration.** `UploadSessionsController.MultipartHeaderLimits
   .MaxChunkRequestBodyBytes` is `32L * 1024 * 1024`, independent of the configurable
   `ResumableUpload:MaxChunkSizeBytes` option (default 8 MiB). Changing the configured
   chunk size does not change this framework-level cap.
4. **The per-chunk size check is a no-op for chunked-transfer-encoding requests.**
   `AppendChunk` derives the declared chunk length from `Request.ContentLength ?? 0`;
   when `Content-Length` is absent, the declared length is `0`, which never exceeds
   `MaxChunkSizeBytes`, so `ResumableUploadService.AppendChunkAsync`'s per-chunk check
   is bypassed for such requests. Only the cumulative session-total cap in
   `FileSystemStorage.AppendChunkAsync` still applies in that case.
5. **The per-session lock dictionary is never cleaned up.**
   `FileSystemStorage.UploadSessionLocks` is a `static
   ConcurrentDictionary<string, SemaphoreSlim>`; entries are added on first chunk
   append and never removed on finalize, abort, or expiry — an unbounded, long-lived
   in-process leak under sustained resumable-upload traffic.
6. **Admin bypass is inconsistent with the rest of the app.** `GetStatusAsync` allows
   an admin to view any user's session, but `AppendChunkAsync`, `FinalizeAsync`, and
   `AbortAsync` all check `IsOwnedBy` with no admin bypass — unlike the
   admin-sees-everything pattern used elsewhere (e.g. `FileAccessService` for ordinary
   downloads, §6 above).
7. **`FinalizeAsync` re-hashes the whole assembled file** via
   `FileSystemStorage.ComputeChecksumAsync`, a full second read pass over the temp
   file, instead of maintaining an incremental SHA-256 across the `AppendChunkAsync`
   writes the way `UploadFileService`/`FileSystemStorage.SaveNewAsync` already does
   for the one-shot upload path (§5 above) — extra I/O proportional to file size on
   every finalize.
8. **`CommitWithCollisionRetryAsync` is duplicated**, not shared: the same 5-attempt
   storage-key-collision-retry loop exists separately in both `UploadFileService` and
   `ResumableUploadService`.

None of these affect the one-shot upload path. The distributed-lock gap (item 1's
sibling limitation — the lock that *does* exist is per-instance only) is also called
out in the README "Known limitations" section, along with all eight items above.
