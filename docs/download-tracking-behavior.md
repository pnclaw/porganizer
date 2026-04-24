# Download Tracking Behavior

## Downloads Not Tied to a Video

When a download is sent to a download client without an `IndexerRowId`, **no `DownloadLog` entry is created**. The send endpoint (`/api/download-clients/{id}/send`) only writes a `DownloadLog` row when `request.IndexerRowId.HasValue` is true. If omitted, only an `IndexerApiRequest` tracking record is written — the download goes to the client untracked.

This is enforced at the schema level: `DownloadLog.IndexerRowId` is **not nullable**, so the database cannot hold a log entry without a link to an `IndexerRow`. An `IndexerRow` does not need to have an `IndexerRowMatch` — a `DownloadLog` can exist with no prdb.net video link at all.

## File Hashing

OsHash (OpenSubtitles algorithm) is computed for each video file when a download reaches `Completed` status, stored in `DownloadLogFile.OsHash`.

- Algorithm: file size + sum of uint64 chunks from first 64 KB + sum of uint64 chunks from last 64 KB
- Result: 16-character lowercase hex string
- Files under 128 KB → `null` (too small)
- Only files with extensions in `VideoExtensions.All` are hashed
- `DownloadLogFile.PHash` exists but is reserved and unused

## Download ↔ Download Log Link

| Field | Type | Purpose |
|---|---|---|
| `DownloadLog.IndexerRowId` | NOT NULL FK | Links log entry to the source IndexerRow |
| `DownloadLog.ClientItemId` | nullable | ID returned by the download client (e.g. SABnzbd `nzo_id`); used by the poller to match running jobs back to their log row |
| `DownloadLogFile.DownloadLogId` | NOT NULL FK (CASCADE) | Links each file to its parent log entry |
| `DownloadLogFile.PrdbDownloadedFromIndexerFilenameId` | nullable | Links a file to a prdb.net indexer filename when matched |

Items with a null `ClientItemId` are never polled for status updates.

## Polling Loop and prdb.net Notifications

`DownloadPollingWorker` runs `DownloadPollService.PollAsync` every 20 seconds. For each cycle:

1. **Poll download client** (SABnzbd/NZBGet API) — fetches current job statuses.
2. **Update `DownloadLog`** — status, bytes downloaded, storage path, `CompletedAt`, etc.
3. Save to database.
4. For each log that just reached `Completed`, in order:
   1. **`DownloadLogFileSyncService`** — scans the filesystem at `StoragePath`, creates `DownloadLogFile` records and computes OsHash.
   2. **`DownloadFileMoveService`** — moves files to the site folder and queues a library reindex.
   3. **`FulfillWantedVideosAsync`** — looks up `IndexerRowMatch` → `PrdbWantedVideo`; if an unfulfilled match is found, marks it fulfilled locally then calls prdb.net.

The only prdb.net call in this flow is the fulfillment notification, which fires **after** the file move:

- **`PUT /wanted-videos/{videoId}`** — sends `isFulfilled: true`, `fulfilledAtUtc`, `fulfilledInQuality`, and `fulfillmentExternalId` (the `DownloadLog` ID).

If there is no `IndexerRowMatch` or no matching unfulfilled `PrdbWantedVideo`, no prdb.net call is made. HTTP errors from prdb.net are logged as warnings and do not interrupt processing.

## What Happens When No IndexerRowMatch Exists

A `DownloadLog` can exist without an `IndexerRowMatch` — there is no FK or schema constraint linking the two. When no match exists for a completed download:

- **File move is skipped** — the move service looks up `IndexerRowMatch` via `IndexerRowId`; if none is found it logs a warning and skips that entry, leaving files in the raw download folder.
- **Wanted video fulfillment is skipped** — the poll service queries `IndexerRowMatch` for completed logs; if no matches are found it exits early without error.
- **No prdb.net notifications are sent.**

Neither the Send endpoint nor the auto-fulfillment service checks for an `IndexerRowMatch` before creating a `DownloadLog`.

## prdb.net Calls During Library Indexing

Library indexing itself (scanning files, computing hashes, matching against `PrdbVideoFilehashes`) makes no prdb.net calls. Calls happen in the post-indexing pipeline, both conditional on the file being matched to a prdb.net video in the database.

**During thumbnail generation** (requires `PrdbApiKey`):
- `GET /videos/{videoId}/user-images` — checks whether the video already has user images on prdb.net. If it does, sprite sheet generation is skipped.

**During image upload** (requires `VideoUserImageUploadEnabled` and `PrdbApiKey`):
- `GET /videos/{videoId}/user-images` — same pre-upload check.
- `POST /video-user-images` (multipart) — uploads each generated preview image and the sprite sheet, with `VideoId`, `PreviewImageType`, `DisplayOrder`, and `BasedOnFileWithOsHash`.

If a file has no video match in the database, or `PrdbApiKey` is not configured, no prdb.net calls are made.

## prdb.net Calls for Downloaded-From-Indexer Sync

`PrdbDownloadedFromIndexerSyncService` runs as part of the `QuickSyncWorker` periodic cycle. It reports completed downloads back to prdb.net so the site can record which indexer NZB was used to fulfil a video.

For each completed `DownloadLog` that hasn't been synced yet (or had a previous sync error), it:

1. Runs `DownloadLogFileSyncService` to refresh the file list from disk.
2. Skips the log if it has no `IndexerRowMatch`, no `ClientItemId`, or no files on disk.
3. Skips if a fingerprint (SHA256 of all relevant fields) matches the last successful sync — nothing to update.
4. Calls prdb.net:

| Scenario | Endpoint | Method | Data sent |
|---|---|---|---|
| First sync for this log | `POST /downloaded-from-indexers` | POST | `VideoId`, `IndexerSource`, `NzbId`, `DownloadIdentifier` (client item ID), `NzbName`, sanitized `NzbUrl`, filenames with `Filesize`/`OsHash`/`PHash` |
| Re-sync after error or change | `PUT /downloaded-from-indexers/{id}` | PUT | Same parent fields (no filenames) |
| New files found on re-sync | `POST /downloaded-from-indexers/{id}/filenames` | POST | `Filename`, `Filesize`, `OsHash`, `PHash` |
| Existing file data changed | `PUT /downloaded-from-indexers/{id}/filenames/{id}` | PUT | Same |
| File removed from disk | `DELETE /downloaded-from-indexers/{id}/filenames/{id}` | DELETE | — |

Logs that have **no `IndexerRowMatch`** go through the same flow with `videoId` sent as `null`. All other fields (`IndexerSource`, `NzbId`, `DownloadIdentifier`, filenames) are still required and behave identically.

**When DELETE is triggered:** On each sync run, `DownloadLogFileSyncService` rescans the storage path and compares against the existing `DownloadLogFile` records. If a record exists in the database but its file is no longer found on disk, the record is removed locally and — if it was already synced to prdb.net (`PrdbDownloadedFromIndexerFilenameId` is set) — a DELETE call is made to remove it from prdb.net too.

This can only happen **before** files are moved to their target folder. Once `DownloadLog.FilesMovedAtUtc` is set, the file sync short-circuits and returns the existing records as-is without touching the filesystem — the shared site folder is not re-scanned because it contains other downloads' files too. So a file move never triggers deletes on prdb.net.

The indexer API key is redacted to `[apikey]` in the NZB URL before being sent. Only DrunkenSlug and NzbFinder are supported as `IndexerSource` values; any other indexer URL results in a sync error recorded on the log.

## Design Intent

The system tracks any download that originates from an indexed NZB (`IndexerRow`), regardless of whether that row has been matched to a prdb.net video. File moves and wanted fulfillment are opportunistic — they apply when a match exists and are silently skipped otherwise. Downloads sent without an `IndexerRowId` are fire-and-forget with no database record.
