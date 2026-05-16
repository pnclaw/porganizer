# TODO / Future Work

## Download backend reliability

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
