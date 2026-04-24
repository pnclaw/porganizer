# Video User Image Sprite Sheet Uploads

This document describes the public API contract for external clients that submit sprite-sheet preview images.

## What Changed

`POST /video-user-images` still accepts `multipart/form-data` and still uses the existing `file` form field for the JPEG image.

When `previewImageType` is `SpriteSheet`, clients must now upload a second file:

- `file`: the JPEG sprite-sheet image
- `vttFile`: the paired WebVTT file

The API derives `hasVtt` server-side. Clients should no longer send a `hasVtt` form value.

## Single Image Upload

Use `previewImageType=Single` when uploading a normal one-frame preview image.

Required form fields:

- `file`: JPEG image
- `videoId`: video GUID
- `basedOnFileWithOsHash`: 16-character hexadecimal OS hash
- `previewImageType`: `Single`
- `displayOrder`: zero or a positive integer

Do not include `vttFile` for `Single` uploads. The API rejects `Single` uploads that include a VTT file.

Example:

```http
POST /video-user-images
Content-Type: multipart/form-data
X-Api-Key: <api-key>

file=@preview.jpg
videoId=00000000-0000-0000-0000-000000000000
basedOnFileWithOsHash=ABCDEF1234567890
previewImageType=Single
displayOrder=0
```

## Sprite Sheet Upload

Use `previewImageType=SpriteSheet` when uploading a JPEG sprite sheet with WebVTT cue metadata.

Required form fields:

- `file`: JPEG sprite-sheet image
- `vttFile`: WebVTT file with a `.vtt` filename
- `videoId`: video GUID
- `basedOnFileWithOsHash`: 16-character hexadecimal OS hash
- `previewImageType`: `SpriteSheet`
- `displayOrder`: zero or a positive integer

Validation rules:

- `file` must be a valid JPEG image.
- `file` must not exceed the configured image byte limit.
- `file` dimensions must not exceed the configured image dimension limit.
- `vttFile` is required.
- `vttFile` must be non-empty.
- `vttFile` must not exceed the configured VTT byte limit.
- `vttFile` filename must end in `.vtt`.
- `vttFile` content must start with `WEBVTT`, allowing whitespace or a UTF-8 BOM before it.

Example:

```http
POST /video-user-images
Content-Type: multipart/form-data
X-Api-Key: <api-key>

file=@spritesheet.jpg
vttFile=@spritesheet.vtt
videoId=00000000-0000-0000-0000-000000000000
basedOnFileWithOsHash=ABCDEF1234567890
previewImageType=SpriteSheet
displayOrder=1
```

## Storage And Read Responses

The API stores the JPEG and VTT objects under the same Bunny CDN basename:

```text
ugc/video-user-images/{firstVideoIdChar}/{secondVideoIdChar}/{videoId}/{randomGuid}.jpg
ugc/video-user-images/{firstVideoIdChar}/{secondVideoIdChar}/{videoId}/{randomGuid}.vtt
```

The database stores the JPEG path as `cdnPath` and stores `hasVtt=true` for sprite sheets. The VTT path is derived from the JPEG path by changing the extension from `.jpg` to `.vtt`.

Read endpoints now include `vttUrl`:

- `GET /video-user-images/{videoUserImageId}`
- `GET /videos/{videoId}/user-images`
- `GET /video-user-images/changes`

For `Single` images, `vttUrl` is `null`.

For `SpriteSheet` images, `vttUrl` is the public CDN URL for the paired `.vtt` object.

Example response fields:

```json
{
  "previewImageType": "SpriteSheet",
  "url": "https://cdn.example/ugc/video-user-images/a/b/ab000000-0000-0000-0000-0000000000bb/0123456789abcdef0123456789abcdef.jpg",
  "hasVtt": true,
  "vttUrl": "https://cdn.example/ugc/video-user-images/a/b/ab000000-0000-0000-0000-0000000000bb/0123456789abcdef0123456789abcdef.vtt"
}
```

## Error Cases

The API returns `400 Bad Request` when:

- `previewImageType=SpriteSheet` is submitted without `vttFile`
- `previewImageType=Single` is submitted with `vttFile`
- the VTT file is empty, too large, has the wrong extension, or does not start with `WEBVTT`
- the JPEG validation fails

The duplicate rule is unchanged: submitting the same authenticated user, video, and `basedOnFileWithOsHash` combination returns `409 Conflict`.
