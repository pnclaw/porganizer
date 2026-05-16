# Changelog

Entries are grouped by feature branch, newest first.
See [`docs/changelog/`](docs/changelog/) for archived entries.

## bugfix/optional-download-client-port — 2026-05-16

### Done
- Made `Port` nullable (`int?`) on `DownloadClient` and all related request/response DTOs.
- URL builders in `DownloadClientTester`, `DownloadClientSender`, `SabnzbdPoller`, and `NzbgetPoller` now omit the `:port` segment when no port is set, letting the scheme default (80/443) apply.
- Added EF migration `MakeDownloadClientPortNullable`.
- Enables SABnzbd and NZBGet instances hosted behind a reverse proxy (e.g. nginx proxy manager) to be configured with just a hostname and no explicit port.

### Dead Ends
- *(none)*

## feature/transfer-downloaded-files-after-failure — 2026-05-15

### Done
- Added a new Rescue feature (`POST /api/rescue/preview` and `POST /api/rescue/execute`) that scans a user-supplied folder, matches each direct subfolder name against known indexer rows using the same title-normalisation logic as the existing pipeline, and moves matched files to the configured target library folder.
- Execute creates or reuses a `DownloadLog` record, populates `DownloadLogFile` entries, delegates the actual file move to the existing `DownloadFileMoveService`, and marks any linked wanted video as fulfilled.
- Added a dedicated Rescue page in the frontend (folder input → Preview → confirm → Execute with per-item result log) accessible via the Admin nav group.
- Added 4 integration tests covering: bad-folder 400 on preview, bad-folder 400 on execute, matched subfolder returns correct video/site, unmatched subfolder returns unmatched item.

### Dead Ends
- *(none)*

## feature/video-detail-view — 2026-05-15

### Done
- Added `IndexerTitle` field to the video detail indexer-matches response so the UI can show which indexer each match came from.
- Added integration tests for `GET /api/prdb-videos/{id}/indexer-matches`: happy-path verifying the indexer title is returned, and a 404 sad-path for an unknown video ID.

### Dead Ends
- *(none)*

## feature/newznab-attr-guid — 2026-05-08

### Done
- Fixed NZB ID extraction for indexers (e.g. nzbporn) that supply the canonical ID via `newznab:attr name="guid"` rather than in the `<guid>` element URL. The parser now reads the attr value directly as the NzbId, avoiding brittle URL parsing. Indexers like DrunkenSlug that omit the attr fall back to the existing path-segment extraction unchanged.

### Dead Ends
- *(none)*

## feature/ember-tidal-grove — 2026-05-08

### Done
- Fixed indexer backfill saving only one row for indexers whose Newznab guid uses a query-string ID (e.g. `?id=abc123`). `ExtractId` was returning the last URL path segment for all items, collapsing every NZB to the same ID and discarding all but the first. It now checks for `id`, `ID`, `nzbid`, and `nzbID` query parameters before falling back to the path segment.

### Dead Ends
- *(none)*

## feature/auto-cleanup-improvements — 2026-04-23

### Done
- Fixed manual cleanup (Advanced Settings > Library) not including files where prdb.net already had images before this app could upload them. These files were marked `SkippedPrdbAlreadyHasImages` but had no `VideoUserImageUploads` rows, so the eligibility query never matched them. The query now treats `SkippedPrdbAlreadyHasImages` as a second eligible condition alongside the existing upload-count check.
- Added three new unit tests covering the `SkippedPrdbAlreadyHasImages` path: preview returns file, nothing-on-disk exclusion, and delete clears all local files.

### Dead Ends
- *(none)*

## feature/usenet-search — 2026-04-19

### Done
- Added `PrdbIndexerFilehash` entity and EF migration to store filehashes submitted by indexer users, synced from the new prdb.net `/indexer-filehashes` endpoints. Includes backfill (1 000 rows/run) and incremental seek-cursor sync with soft-delete support.
- Formalized `IndexerSource` enum (DrunkenSlug=0, NzbFinder=1) and extracted `IndexerSourceMapper` to `Common` for reuse across sync and search slices.
- Added Indexer Filehash Sync status card to the admin Sync Status page with Run Now and Reset Cursor buttons.
- Added `GET /api/usenet-search` endpoint: paginated indexer row search with keyword and indexer filters, video match hints, filehash link hints, and preview images. Resolves images from `PrdbVideoUserImage` (Single/Public) with fallback to `PrdbVideoImage` CDN paths.
- Added Usenet Search frontend page (`/usenet/search`) with two display modes: a preview card grid and a text table. Both support indexer multi-select and keyword search. Sends NZBs to configured download clients.

### Dead Ends
- *(none)*

## feature/filehash-sync-changes — 2026-04-19

### Done
- Fixed incremental filehash sync missing records that were updated on prdb.net during a long backfill. The cursor was previously set to `DateTime.UtcNow` after backfill completed, so any `VideoId` assignments that happened while the backfill was running were permanently skipped by the incremental feed. The cursor is now set to 5 minutes before the backfill started, ensuring the first incremental run re-checks the overlap window.
- Added integration test asserting the backfill completion cursor is set before the run started, not after.

### Dead Ends
- *(none)*

## feature/preview-improvements — 2026-04-19

### Done
- Added `DownloadLibraryPath` app setting: a user-configured root folder for completed downloads that are not moved to a library folder (move disabled or unmatched file). When set, the folder is automatically registered as a `LibraryFolder` on startup and after each settings save.
- `DownloadPollService` now enqueues the (folder-mapped) `StoragePath` of every completed download for library indexing after the move step, so newly landed files are picked up immediately without waiting for the 24-hour `SyncWorker` cycle.
- Folder mappings are applied to `StoragePath` before enqueuing, matching the behaviour of `DownloadFileMoveService`.
- Exposed `DownloadLibraryPath` in the Settings API (`GET`/`PUT /api/settings`) and added a "Download library path" text field in the Downloads settings tab of the UI.
- Fixed `PrdbApiVideoUserImageDto.VideoId` deserialization crash: made the field `Guid?` so the sync service handles images uploaded for unmatched files (where the API returns `null` for `videoId`) without throwing.
- Added integration tests for `DownloadLibraryPath`: default null, persists correctly, auto-creates `LibraryFolder`, and is idempotent.

### Dead Ends
- *(none)*

## feature/thumbnail-preview-nonvideo — 2026-04-19

### Done
- Extended preview image generation to cover unmatched library files (no `VideoId`), controlled by the existing `PreviewImageGenerationMatchedOnly` setting.
- Sprite sheet generation remains strictly matched-only; enforced in `ThumbnailGenerationService` and the `ThumbnailWorker` startup enqueue.
- Added `GetUserImageCountByOsHashAsync` to `IPrdbUserImageCheckService` using the new `GET /video-user-images/by-os-hash/{osHash}` endpoint for the pre-upload duplicate check on unmatched files.
- Updated `VideoUserImageUploadService` to upload unmatched files (5 single-frame previews, no sprite sheet, no `VideoId` field) using the updated prdb.net API that now accepts `videoId: null`.
- Made `VideoUserImageUpload.PrdbVideoId` nullable with an EF migration.
- Updated `PreviewWorker.MaybeEnqueueUploadAsync` so unmatched files trigger upload after previews alone; matched files still wait for both previews and sprite sheet.
- Updated the Settings UI: corrected the "Upload to prdb.net" description to cover unmatched files, and removed the `thumbnailGenerationEnabled` requirement from the upload toggle's disabled condition.
- Added integration tests for unmatched-file upload happy path and prdb-already-has-images skip.

### Dead Ends
- Initially assumed the prdb.net `POST /video-user-images` endpoint required `VideoId`; the API team updated the spec to make it optional, with images grouped by `BasedOnFileWithOsHash` when omitted.

## feature/sending-hashes-nonvideo — 2026-04-19

### Done
- Extended `PrdbDownloadedFromIndexerSyncService` to sync completed downloads that have no `IndexerRowMatch`: these now go through the same `POST /downloaded-from-indexers` flow with `videoId: null`, using all the existing state fields and re-sync logic.
- Changed `AddDownloadedFromIndexerRequest.VideoId` and `CreateParentAsync`/`ComputeFingerprint` signatures from `Guid` to `Guid?` to match the updated prdb.net API contract.
- Added an integration test confirming the null-videoId path sends `"videoId":null` and correctly stores the returned parent and filename IDs.

### Dead Ends
- Initially implemented a separate `POST /videos/filehashes` endpoint call with a new `PrdbUnlinkedFilehashId` column on `DownloadLogFile`. Scrapped after discovering the API spec was updated to make `videoId` nullable on `POST /downloaded-from-indexers` instead, removing the dedicated endpoint.

## feature/download-log-ideas — 2026-04-18

### Done
- Corrected `docs/download-tracking-behavior.md`: clarified that `DownloadLog` links to an `IndexerRow`, not directly to a video; `IndexerRowMatch` is optional with no schema constraint.
- Added documentation of the full polling loop sequence and the single prdb.net call it makes (`PUT /wanted-videos/{id}` after file move).
- Added documentation of prdb.net calls made during library indexing (thumbnail check, image upload).
- Added documentation of `PrdbDownloadedFromIndexerSyncService`: when it runs, all HTTP calls it makes, and when DELETE is triggered vs. suppressed (guarded by `FilesMovedAtUtc`).

### Dead Ends
- *(none)*

## feature/db-viewer-improvements — 2026-04-18

### Done
- Added **SQL Query** tab to the Admin Database page alongside the existing Table Browser tab.
- `POST /api/admin/database/query` executes arbitrary SQL (SELECT or mutating) and returns `{ columns, rows, rowsAffected }`.
- `DatabaseQueryView.vue`: multi-line monospace SQL textarea, Ctrl+Enter shortcut, query history dropdown (localStorage, 20 entries, with clear-history action), results rendered as a sortable `v-data-table`, "N rows affected" message for non-SELECT, error alert on SQL failure.
- 4 new integration tests: SELECT returns columns/rows, non-SELECT returns rowsAffected, invalid SQL returns 400, empty SQL returns 400.

### Dead Ends
- *(none)*

## feature/log-viewer-improvements — 2026-04-18

### Done
- Added read-only SQLite database viewer under Admin → Database (`/admin/database`).
- `GET /api/admin/database/tables` lists all user-defined tables from `sqlite_master`.
- `GET /api/admin/database/tables/{table}/rows` returns paginated rows; accepts optional `WHERE` clause, `ORDER BY` column (validated against `PRAGMA table_info` to prevent injection), `orderDir` (asc/desc), `page`, and `pageSize` (capped at 1000).
- `DatabaseView.vue`: table selector, free-text WHERE input (runs on Enter or Run button), ORDER BY column dropdown and ASC/DESC toggle, configurable page size (20/50/100/250/500), `v-data-table-server` with server-side pagination.
- 7 integration tests covering table listing, pagination, WHERE filtering, valid/invalid ORDER BY, and unknown table rejection.

### Dead Ends
- *(none)*

## feature/log-viewer — 2026-04-18

### Done
- Added `GET /api/app-logs` to list daily rolling log files (filename, date, size)
- Added `GET /api/app-logs/{filename}?search=` to read lines from a single file with optional server-side case-insensitive filter
- Added `DELETE /api/app-logs?retain=all|last7|today` to prune log files by retention policy
- Path-traversal protection on the filename parameter
- Reads active log files safely with `FileShare.ReadWrite` so Serilog is not blocked
- Added `AppLogsView.vue` wired to the existing `/admin/logs` nav entry: file selector, debounced search, scrollable monospace display, delete actions with confirmation dialogs
- 10 integration tests covering list, line retrieval, search, path traversal, and all three deletion modes

### Dead Ends
- *(none)*

## feature/log-file-viewer — 2026-04-18

### Done
- Refactored sidebar navigation into collapsible `v-list-group` sections (Usenet, PRDB, Library) with open/closed state persisted in `localStorage`; groups default open, Admin group defaults closed.
- Added a pinned Admin group at the bottom of the drawer (Settings, Sync Status, Logs placeholder) using Vuetify's `#append` slot.
- Moved PRDB Status into the Admin group, renamed to "Sync Status" at route `/sync-status`; added prdb.net API health card to the top of that page explaining the check is against `GET /health` (no auth).
- Removed the standalone Health nav item and `HealthView.vue`; `/health` route removed entirely.
- Removed `PrdbStatusView.vue`; content lives in `features/admin/SyncStatusView.vue`.
- Child nav items carry icons; group header rows are plain text only; Vuetify indent on nested items zeroed out via `--indent-padding` CSS override.

### Dead Ends
- *(none)*

---

## feature/auto-download-second-quality — 2026-04-17

### Done
- Added `AutoAddAllNewVideosFulfillAllQualities` setting (Advanced Settings → Wanted tab); when enabled, any video auto-added via the "auto-add all new matched videos" feature is queued for download in every available quality (720p, 1080p, 2160p), rather than just the preferred quality.
- New `FulfillAllQualities` flag on `PrdbWantedVideo` is stamped at auto-add time so the fulfillment service knows to use all-qualities mode for that specific record.
- `WantedVideoFulfillmentService` now runs two passes per cycle: the existing single-best-quality pass for normal wanted videos, and a new per-quality pass for `FulfillAllQualities` records that queues each quality independently (skipping any quality that already has a download log in any status, preserving the manual-retry protection).
- Fixed a latent test infrastructure bug: `StubHttpClientFactory` was returning the same `HttpClient` instance for every `CreateClient` call, causing `.Timeout` to throw on the second use; it now creates a fresh instance per call.
- EF migration `AddFulfillAllQualities` adds the two new columns.
- Integration tests cover happy paths (all three qualities queued, partial matches, already-logged qualities skipped, fulfilled video skipped) and settings persistence.

### Dead Ends
- *(none)*

---

## feature/video-library-previews — 2026-04-17

### Done
- Added `PrdbVideoUserImage` entity and `PrdbVideoUserImageSyncService` to sync user-submitted preview images from prdb.net via the `/video-user-images/changes` delta feed with cursor-based incremental sync
- Library list thumbnail now uses `PrdbVideoUserImage` (Single, Public) as primary source with `PrdbVideoImage` as fallback; hover sprite sheet animation uses CDN URL from synced `SpriteSheet` user image — no local file URLs used for display
- Library detail view shows three separate image sections: sprite sheet card (auto-rotating), user images carousel, and original prdb video images carousel; all sourced from prdb.net CDN
- Sprite grid layout (`spriteColumns`, `spriteRows`, `spriteTileCount`) sourced directly from prdb.net API fields, removing all dependency on local library files for display
- Added `docs/prdb-video-image-types.md` documenting the two image types and display rules

### Dead Ends
- Initially considered using local `LibraryFile.SpriteSheetTileCount` for sprite animation — abandoned in favour of the `spriteTileCount`/`spriteColumns`/`spriteRows` fields added to the prdb.net `VideoUserImageDto`
- Considered parsing the WebVTT file to derive tile count — superseded by the API adding the fields directly

---

## feature/thumbnail-check-before — 2026-04-17

### Done
- Before running ffmpeg to generate a sprite sheet, check `GET /videos/{id}/user-images` on prdb.net for matched files; skip generation if the video already has user images
- Extracted `IPrdbUserImageCheckService` into `Common/Prdb` so the check is shared between `ThumbnailGenerationService` and `VideoUserImageUploadService`
- When thumbnail generation is skipped due to existing prdb images, stamp `VideoUserImageUploadCompletedAtUtc` so the upload service does not keep retrying
- Prdb check fails open: a network/API error proceeds with generation rather than blocking it
- Updated and extended unit tests for both services

### Dead Ends
- *(none)*

---

## feature/skipped-thumbnail-uploads — 2026-04-17

### Done
- Added explicit preview image upload completion state to `LibraryFile`, including the completion reason and remote image count.
- When prdb.net already has user images for a video, `VideoUserImageUploadService` now records the file as completed with a skipped reason instead of leaving it pending forever.
- Updated PRDB status pending counts and the preview `upload-all` queue to use completion state, while keeping actual uploaded image counts based on real `VideoUserImageUploads` rows.
- Added an EF migration that backfills completion state for already fully uploaded local files.
- Added tests covering skipped PRDB-existing images, completed upload-all exclusion, and partial uploads remaining pending.

### Dead Ends
- Considered inserting fake `VideoUserImageUploads` marker rows for skipped files, but rejected it because that table represents actual image uploads and marker rows would pollute uploaded image counts and cleanup semantics.

---

## feature/downloadlog-createdat-fix — 2026-04-16

### Done
- Fixed automatic wanted-fulfillment download logs being created with default `CreatedAt` / `UpdatedAt` values, which rendered as `1/1/1, 12:00:00 AM` for the Started timestamp. A migration repairs existing affected download logs from their completion, last-polled, or updated timestamp.

### Dead Ends
- *(none)*

---

## feature/download-log-issue — 2026-04-16

### Done
- Added diagnostic logging to `DownloadPollService` to surface poll misses: logs a warning with the hash and matched download log ID whenever a polled item is not found in the client's current queue.
- Added a runtime log level setting to Settings → Advanced → General. A dropdown lets the user choose Verbose / Debug / Information / Warning / Error / Fatal; the change takes effect immediately via `LoggingLevelSwitch` with no restart required. The selected level is persisted in `AppSettings` and restored on startup.

### Dead Ends
- *(none)*

---

## feature/thumbnail-path-issues — 2026-04-16

### Done
- Fixed `ffprobe` missing from the Docker runtime image. The `ffmpeg-downloader` stage now extracts and copies both `ffmpeg` and `ffprobe` from the johnvansickle.com static tarball; the runtime stage copies both binaries. Previously, thumbnail generation always failed with "No such file or directory" for `ffprobe`.
- Added diagnostic logging to `DownloadLogFileSyncService` and `DownloadFileMoveService` to expose quote-in-filename issues: scanned filenames now log whether they have leading/trailing single quotes, a failed source-file lookup dumps all sibling filenames in the folder, and the move service now logs `storagePath`, `sourceFolder`, `destName`, and the final `moved → dest` path at Info level.
- Updated `finish-feature` command to run `git fetch --all` before comparing branches.

### Dead Ends
- *(none)*

---

## feature/more-stability — 2026-04-16

### Done
- Fixed login always showing "Invalid username or password." even on success. The `Login` and `Logout` endpoints were returning `200 OK` with an empty body; the `fetch` helper in `api.ts` only skips JSON parsing for 204/202, so it threw a `SyntaxError` that landed in the `catch` block and triggered the error message. Both endpoints now return `204 No Content`.
- Fixed moved files keeping surrounding single quotes in their names (e.g. `'video.mkv'` → `video.mkv`). NZB archives can extract files with quote-wrapped names; the move service now strips leading/trailing single quotes when computing the destination file name.
- Fixed stale `fileNames: string[] | null` in the `DownloadLog` TypeScript interface. The `AddDownloadLogFiles` migration replaced the JSON column with a relational table; the interface now uses `files: DownloadLogFile[] | null` to match the actual API response. The "Extracted Files" section in the download detail dialog now renders correctly.

### Dead Ends
- *(none)*

---

## feature/downloaded-move-retry — 2026-04-16

### Done
- Added a "Move files" button to the download detail dialog. It appears only for completed downloads that have not yet been moved, and calls the new `POST /api/download-logs/{id}/move` endpoint.
- The move endpoint runs the existing `DownloadFileMoveService` logic for a single download and returns the updated log alongside a list of per-step log entries (source → destination paths, warnings for missing files or folders, errors for failed moves).
- `DownloadFileMoveService.MoveAsync` now returns `IReadOnlyList<MoveLogEntry>` with human-readable messages at Info / Warning / Error level. The automated poll path ignores the return value so behaviour is unchanged there.
- After pressing the button the frontend renders the entries as a colour-coded scrollable list inside the detail dialog (blue info, amber warning, red error).

### Dead Ends
- *(none)*

---

## feature/login-fix — 2026-04-16

### Done
- Fixed a bug where logging in with auth enabled would show an error even though the login succeeded. The `fetch` helper in `api.ts` was missing `credentials: 'same-origin'`, so the auth cookie was never attached to subsequent requests. The immediate post-login status check saw no cookie, returned unauthenticated, and the router redirected back to the login page. A manual reload worked because the browser would include the cookie on the next full navigation.

### Dead Ends
- *(none)*

---

## feature/hard-reset-improvment — 2026-04-16

### Done
- `POST /api/settings/reset-prdb-data` now deletes all thumbnail sprite sheets and preview images from disk in addition to clearing the database rows. Previously, the `{dataDir}/thumbnails/` and `{dataDir}/previews/` directories were left fully populated after a reset.
- Added integration test `Reset_DeletesThumbnailAndPreviewFilesFromDisk` verifying both cache directories are emptied by the reset endpoint.

### Dead Ends
- *(none)*

---

## feature/ffmpeg-static-binary — 2026-04-16

### Done
- Replaced `apt-get install ffmpeg` in the Docker runtime stage with a dedicated `ffmpeg-downloader` stage that downloads a static binary from johnvansickle.com.
- The downloader stage runs with `--platform=$BUILDPLATFORM` so it always executes natively on the build machine — QEMU emulation is never involved, even when cross-compiling for arm64.
- The runtime image no longer runs `apt-get` at all; it simply copies the single static binary from the downloader stage.
- Eliminates the ~50-package transitive dependency chain that was causing 15+ minute CI builds on the arm64 QEMU leg.

### Dead Ends
- *(none)*

---

## feature/complete-reset-function — 2026-04-16

### Done
- Added "General" tab to Advanced Settings with a "Reset database" button and two-step confirmation dialog.
- Backend `POST /api/settings/reset-prdb-data` now clears all operational and synced data: indexer rows, indexer API request log, download logs and files, all PRDB-cached data (networks, sites, videos, actors, wanted list, filehashes, pre-DB entries), library file records, index requests, and video upload tracking.
- All sync cursors and run-at timestamps in `AppSettings` are reset (including `FavoritesWantedLastRunAt` and `AutoAddAllNewVideosLastRunAt` which were previously omitted). Indexer backfill state is also cleared.
- Preserved: download client settings, indexer credentials/config, library folder paths, folder mappings, and all `AppSettings` configuration values (API key, quality preference, etc.).
- Deletion order respects FK constraints throughout — children deleted explicitly before parents.
- 5 integration tests covering: empty-DB reset, full operational-data deletion, protected-data preservation, sync-cursor reset, and indexer backfill state reset.

### Dead Ends
- *(none)*

---

## feature/frost-lamp-tide — 2026-04-12

### Done
- Fixed auto-add wanted list flooding: `PrdbVideo.PrdbCreatedAtUtc` was being set to the local sync time (now) rather than the authoritative creation date from prdb.net, causing the days-back filter to treat every video synced recently as a new release.
- `PrdbVideoDetailSyncService` now writes `detail.CreatedAtUtc` back to `PrdbVideo.PrdbCreatedAtUtc` on each detail sync run, correcting the value without a migration or manual backfill.

### Dead Ends
- *(none)*

---

## feature/copper-wind-shelf — 2026-04-12

### Done
- Fixed a foreign key constraint crash (`SQLite Error 19`) when the filehash sync service tried to insert `PrdbVideoFilehashes` rows whose `VideoId` referenced a video not yet synced locally.
- Removed the DB-level FK constraint on `PrdbVideoFilehashes.VideoId` via migration `RemoveFilehashVideoForeignKey`. The column remains nullable and indexed; `VideoId` is now a soft reference that resolves naturally once the video sync catches up.
- Removed the unused `Video` navigation property from `PrdbVideoFilehash` (it was never loaded in any query).

### Dead Ends
- *(none)*

---

## feature/preview-image-generation-improvement — 2026-04-12

### Done
- Parallelised the 5 preview frame extractions per video using `Task.WhenAll`, reducing per-video preview generation time by ~5×.
- Added optional cookie-based single-user authentication. When `Auth:Enabled` is `true` in `appsettings.json`, all `/api/*` routes return 401 for unauthenticated requests. Auth is off by default so existing deployments are unaffected.
- New `GET /api/auth/status`, `POST /api/auth/login`, and `POST /api/auth/logout` endpoints. Login issues a 30-day persistent cookie.
- Vue SPA gains a `/login` route with a Vuetify login form, a router guard that redirects to `/login` when auth is required, and a Logout item in the nav drawer.
- 10 integration tests covering auth status, login, wrong credentials, protected endpoint enforcement, and logout.

### Dead Ends
- *(none)*

---

## feature/wanted-download-fail-no-retry — 2026-04-12

### Done
- Fixed a bug where a failed download would be re-queued automatically on every fulfillment cycle (every 5 minutes), thrashing the download client with the same NZB.
- `WantedVideoFulfillmentService` now treats any existing `DownloadLog` entry — including those with status `Failed` — as a signal to skip the video. Failed downloads must be retried manually via the existing recheck endpoint.
- Updated the corresponding unit test to assert the new no-retry behaviour.

### Dead Ends
- *(none)*

---

## feature/preview-image-stats-improvements — 2026-04-12

### Done
- Added `FilesAwaitingPreviewGeneration` and `FilesAwaitingThumbnailGeneration` to `PreviewImageUploadStatus` in `GET /api/prdb-status`; counts respect the `MatchedOnly` settings to mirror what the background workers actually process.
- Added two rows to the Preview Image Upload card in `PrdbStatusView`, with warning-colour highlighting when the count is above zero.
- Added integration test covering the new fields across the three generation states (neither, preview-only, both).

### Dead Ends
- *(none)*

---

## feature/marble-drift-lantern — 2026-04-12

### Done
- Fixed crash in `LibraryIndexingService` when `Directory.GetFiles` returns the same relative path more than once (e.g. via symlinks or junction points); duplicate paths are now skipped with a warning log rather than causing a `UNIQUE constraint failed` error.
- Added `PreviewImageUpload` section to `GET /api/prdb-status`: reports enabled/disabled state, auto-delete setting, files uploaded (distinct), images uploaded (total), files pending upload, and last upload timestamp.
- Added 3 integration tests for the new status section covering zero-state, count correctness, and incomplete-generation exclusion.
- Added Preview Image Upload card to `PrdbStatusView`, positioned between Indexer Row Match and Library Counts; pending count is highlighted in warning colour when non-zero.

### Dead Ends
- *(none)*

---

## feature/advanced-reset — 2026-04-12

### Done
- Fixed crash in `LibraryIndexingService` when a folder has duplicate `RelativePath` rows in the database; the service now logs a warning and continues rather than throwing `ArgumentException`.
- Added a unique index on `(LibraryFolderId, RelativePath)` in `LibraryFiles` to prevent duplicate rows from accumulating in future indexing runs.

### Dead Ends
- *(none)*

---

## feature/auto-delete — 2026-04-12

### Done
- Added `AutoDeleteAfterPreviewUpload` setting: when enabled, the video file, preview images, and sprite sheet are automatically deleted from disk after all 6 images (5 singles + sprite sheet) have been successfully uploaded to prdb.net.
- Auto-delete only fires when the upload is fully complete; partial uploads leave files intact so retries remain possible.
- Added `GET /api/library-cleanup/uploaded-files` endpoint returning all fully-uploaded library files that still have assets on disk, with a total count and bytes-to-free summary.
- Added `POST /api/library-cleanup/delete-uploaded-files` endpoint that executes the cleanup and returns deleted count and freed bytes.
- Added an Advanced Settings page (`/settings/advanced`) with Library and Wanted tabs; accessible via a new "Advanced settings" button on the General tab.
- Manual cleanup UI on the Advanced Settings Library tab: Preview button shows a file list and size estimate, Delete button fires after a confirmation dialog.
- Moved "Auto-add all new matched videos to wanted list" section from the Wanted tab to Advanced Settings → Wanted tab.

### Dead Ends
- *(none)*

---

## feature/auto-add-improvements — 2026-04-12

### Done
- `AutoWantedVideoSyncService` now excludes videos already present in any library folder (`LibraryFiles.VideoId`) from the auto-add candidates, preventing duplicates when the wanted list has been scrubbed.
- `GET /api/download-logs` is now paged (default page size 50) with server-side `search`, `status`, and `activeOnly` filters; response shape changed to `{ items, total }`.
- Download log UI moves filtering and pagination server-side; `v-pagination` appears when results span more than one page.

### Dead Ends
- *(none)*

---

## feature/amber-stone-river — 2026-04-11

### Done
- Install ffmpeg in the Docker runtime image so video preview image generation works out of the box in Docker Compose deployments.

### Dead Ends
- *(none)*

---

## feature/preview-image-upload-improvements — 2026-04-11

### Done
- Fixed "can't access property 'enqueued', result is undefined" error when pressing "Upload All Now": `POST /api/library-previews/upload-all` was returning 202 Accepted, which the `request` helper in `api.ts` treats as a no-body response and returns `undefined`. Changed the endpoint to return 200 OK so the JSON body is parsed correctly.
- Updated two integration tests to assert 200 OK instead of 202 Accepted.

### Dead Ends
- *(none)*

---

## feature/marble-drift-lantern — 2026-04-11

### Done
- New **Video User Image Upload** feature: after both preview images and sprite sheet are generated for a matched library file, porganizer automatically uploads them to prdb.net via `POST /video-user-images`
- Uploads 5 single-frame preview JPEGs (DisplayOrder 0–4, PreviewImageType=Single) and the sprite sheet JPEG with its VTT file (PreviewImageType=SpriteSheet, VttFile multipart field)
- Upload is skipped if the video already has user images on prdb.net, or if an upload record already exists locally
- New `VideoUserImageUpload` entity tracks each uploaded image (LibraryFileId, PrdbVideoId, PrdbVideoUserImageId, PreviewImageType, DisplayOrder, UploadedAtUtc)
- New `VideoUserImageUploadEnabled` setting (bool, default off) with Settings GET/PUT support and a UI toggle in the Settings page
- New `POST /api/library-previews/upload-all` endpoint enqueues all eligible files not yet uploaded; paired with an "Upload All Now" button in the UI showing an enqueued count chip
- Both `PreviewWorker` and `ThumbnailWorker` check after successful generation whether both generation timestamps are set and enqueue the file to the upload queue
- `UploadImageAsync` retries up to 3 times with exponential back-off (2s → 4s → 8s) for transient HTTP errors (503, 502, 504, 429); respects `Retry-After` header for 429
- 4 unit tests for `VideoUserImageUploadService` and 2 integration tests for the `upload-all` endpoint

### Dead Ends
- *(none)*

---

## feature/high-quality-screenshots — 2026-04-10

### Done
- New **Preview Image** generation feature: 5 high-quality JPEG frames extracted per video at 10%, 25%, 50%, 75%, and 90% of duration — avoids black frames at start/end
- Frames are scaled to a max width of 1920px (preserving aspect ratio, height rounded to even); JPEG quality q:v 2 (near-lossless)
- Stored in `{dataDir}/previews/{fileId}/preview_1.jpg` … `preview_5.jpg` — completely separate from the sprite sheet `thumbnails/` folder
- `AppSettings` gains `PreviewImageGenerationEnabled` (bool, default off) and `PreviewImageGenerationMatchedOnly` (bool, default off); exposed through settings GET/PUT
- `LibraryFile` gains `PreviewImagesGeneratedAtUtc` and `PreviewImageCount` with EF migrations `AddPreviewImageSettings` and `AddLibraryFilePreviewFields`
- New `PreviewImageGenerationService` (with `IPreviewImageGenerationService`) and `PreviewQueueService` (bounded channel, same pattern as thumbnails)
- New `PreviewWorker` background service: on startup enqueues files with `PreviewImagesGeneratedAtUtc == null` (respects `MatchedOnly`); processes the queue continuously
- New endpoints on `LibraryThumbnailsController`:
  - `GET /api/library-files/{id}/previews/{n}` — serves preview n (1–5), 404 if not generated or n out of range
  - `POST /api/library-previews/generate-all` — enqueues all files missing previews
  - `POST /api/library-previews/reset-all` — clears DB state and deletes files from disk
- 11 integration tests covering happy-path serve, out-of-range n, no previews generated, count guard, generate-all, and reset-all

### Dead Ends
- *(none)*

---

## feature/video-library-improve-three — 2026-04-10

### Done
- Fixed `PrdbVideoFilehashSyncService` incremental sync missing entries: when the API returned items but a null `NextCursor`, the cursor was never advanced and the same window was re-fetched on every run without progressing. Applied the same `GetCursorFromLastItem` fallback pattern used by `PrdbWantedVideoSyncService`.
- Added regression test covering the null-`NextCursor` case.
- Added **Reset Cursor** button to the Filehash Sync card on the Status page (matching the pattern of all other cursor-based sync cards); backed by new `POST /api/prdb-status/filehash-sync/reset-cursor` endpoint that clears the cursor, resets backfill page to 1, and clears the total count.

### Dead Ends
- *(none)*

---
