# TODO / Future Work

## Download backend reliability

### Avoid duplicate sends after wanted-fulfillment crashes

Wanted fulfillment sends an NZB to the download client before a `DownloadLog` is saved. If SABnzbd
or NZBGet accepts the item but the app stops before `SaveChangesAsync`, the next fulfillment run sees
no log and can enqueue the same download again.

**What needs implementing:**

1. Persist a durable "send pending/in-flight" record before calling the download client, then update
   it with the returned client item ID.
2. Alternatively enforce an idempotency key or unique pending marker per indexer row before send.
3. Add a regression test that simulates send success followed by a DB save failure and verifies the
   next run does not send the same NZB again.

### Serialize or guard concurrent download polling

The scheduled polling worker and on-demand endpoints can call `DownloadPollService.PollAsync`
concurrently. Two polls can load the same log, then save conflicting state transitions from stale
snapshots.

**What needs implementing:**

1. Add a process-wide polling lock, database lease, or optimistic concurrency handling around download
   poll runs.
2. Ensure manual poll/recheck either waits for the active poll or returns a clear "poll already running"
   response.
3. Add regression coverage for overlapping polls where one sees `Completed` and the other sees a
   stale missing/failed result.

### Treat missing download-client item IDs as send failures or recoverable state

SABnzbd send can return success without an `nzo_ids` value. The log is currently saved as `Queued`
with `ClientItemId = null`, but polling excludes logs without a client item ID, so they remain queued
forever.

**What needs implementing:**

1. For clients that require an item ID for polling, treat a successful send response without that ID
   as a failed send, or save a distinct recoverable status.
2. If keeping these logs, add a fallback lookup strategy by NZB name/category/history.
3. Add tests for SABnzbd success responses with missing or empty `nzo_ids`.

### Make folder mapping path-boundary aware

Folder mapping currently uses raw string prefix checks. A mapping such as `/downloads` can also match
`/downloads2`, producing invalid source paths for file sync or file move.

**What needs implementing:**

1. Normalize mapped paths before comparison.
2. Match only exact folder paths or prefixes followed by a directory separator.
3. Centralize folder-mapping logic so download file sync, download file move, and library queueing use
   the same implementation.
4. Add tests for similarly named folders such as `/downloads` and `/downloads2`.

## Re-sync older unverified filehashes

`PrdbVideoFilehash.IsVerified` can transition from `false` → `true` after the initial sync as more
users submit the same file. The 7-day rolling incremental window will catch verification updates on
recently-added entries, but filehashes older than 7 days that later become verified will not be
re-fetched automatically.

**What needs implementing:**

A periodic sweep that queries `PrdbVideoFilehashes` where `IsVerified = false` and `SyncedAtUtc`
is older than some threshold (e.g., 30 days), then re-fetches them from the API to pick up any
verification changes. The `/videos/{id}/filehashes` or `/videos/filehashes/batch` endpoints would
be appropriate for targeted re-checks by video.



## pHash calculation for download log files

`DownloadLogFile` already has a `PHash` column (nullable `TEXT`, max 16 chars) ready for a perceptual hash.

**What needs implementing:**

1. After scanning a completed download's storage path, run FFmpeg-based pHash generation on each video file (`.mkv`, `.mp4`, etc.) — see the OSHash implementation in `src/porganizer.Api/Features/DownloadLogs/OsHash.cs` for reference.
2. The Stash/StashDB algorithm: extract 25 evenly-spaced frames (skipping first/last 5% of video), resize each to 144×144 px, composite into a collage, compute a perceptual hash of the collage. Output is a 16-char hex string.
3. Populate `DownloadLogFile.PHash` alongside `OsHash` in `DownloadPollService.ScanStoragePathsAsync`.
4. Expose `PHash` in `DownloadLogFileResponse` and the frontend.

**References:**
- Stash implementation: `pkg/hash/videophash/phash.go` in [stashapp/stash](https://github.com/stashapp/stash)
- Community tool: [peolic/videohashes](https://github.com/peolic/videohashes)
- StashDB uses Hamming distance ≤ 8 for fuzzy matching
