# PRDB Video Image Types

Two distinct image sources exist for a video on prdb.net. They serve different purposes and are handled differently throughout the application.

---

## PrdbVideoImage — original video images

These are the canonical images attached to a video by the prdb.net editorial team during video ingestion. They are fetched as part of the full video detail sync (`PrdbVideoDetailSyncService`) and stored in the `PrdbVideoImages` table.

- Sourced from: `GET /videos/{id}` (video detail endpoint)
- Synced: during the full periodic sync, not incrementally
- Entity: `PrdbVideoImage` with a `CdnPath` field
- No moderation state — assumed always displayable

## PrdbVideoUserImage — user-submitted preview images

These are images uploaded by any registered prdb.net user for a specific video. They include both still frames ("Single") and sprite sheets ("SpriteSheet"). They are synced incrementally via the delta feed and stored in `PrdbVideoUserImages`.

- Sourced from: `GET /video-user-images/changes` (current-state delta feed)
- Synced: incrementally every 5 minutes via `QuickSyncWorker` using a `(updatedAtUtc, id)` cursor
- Entity: `PrdbVideoUserImage` with `Url`, `PreviewImageType`, `DisplayOrder`, `ModerationVisibility`
- Only images with `ModerationVisibility == "Public"` are displayed
- `PreviewImageType` is either `"Single"` (a still image) or `"SpriteSheet"` (a tiled preview strip)

---

## Display rules

### Library list thumbnail

1. First public `PrdbVideoUserImage` with `PreviewImageType == "Single"`, ordered by `DisplayOrder`
2. Fallback: first `PrdbVideoImage.CdnPath`
3. Fallback: blank placeholder

On hover, the public `PrdbVideoUserImage` with `PreviewImageType == "SpriteSheet"` is overlaid on top of the static thumbnail via CSS `backgroundImage` animation. The grid layout (`spriteColumns`, `spriteRows`) and tile count (`spriteTileCount`) are taken directly from the synced `PrdbVideoUserImage` fields — no local file data is used. The overlay is hidden when no public sprite sheet user image exists.

### Library detail view

| Section | Source | Condition |
|---|---|---|
| Sprite sheet card | Local library file sprite sheet | Hidden if no local sprite sheet exists |
| User images carousel | `PrdbVideoUserImage` (Public, non-SpriteSheet, ordered by DisplayOrder) | Hidden if none |
| Video images carousel | `PrdbVideoImage.CdnPath` | Hidden if none |

The user images carousel and video images carousel are independent — both can be visible at the same time. There is no fallback from one to the other in the detail view.

---

## Why user images are preferred

User images are preferred over original video images because they are more likely to include high-quality scene stills and are kept up to date via incremental sync. The moderation visibility filter (`Public`) ensures only approved images are shown. Original video images remain useful as a fallback for videos that have no user submissions yet.
