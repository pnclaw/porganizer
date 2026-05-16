using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using porganizer.Api.Features.DownloadClients;
using porganizer.Database;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadLogs;

[ApiController]
[Route("api/download-logs")]
[Produces("application/json")]
public class DownloadLogsController(AppDbContext db, DownloadPollService pollService, DownloadFileMoveService downloadFileMoveService, DownloadLogFileSyncService downloadLogFileSyncService) : ControllerBase
{
    [HttpPost("poll")]
    [EndpointSummary("Poll download clients")]
    [EndpointDescription("Immediately polls all download clients for status updates, identical to the scheduled background tick.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Poll(CancellationToken ct)
    {
        await pollService.PollAsync(ct);
        return NoContent();
    }

    [HttpGet]
    [EndpointSummary("List download logs")]
    [EndpointDescription("Returns a paged list of download logs ordered by creation date descending. Optionally filter by search text, status, or active-only.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? status,
        [FromQuery] bool activeOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.DownloadLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(l => EF.Functions.Like(l.NzbName, pattern));
        }

        if (status.HasValue)
            q = q.Where(l => (int)l.Status == status.Value);
        else if (activeOnly)
            q = q.Where(l => l.Status != DownloadStatus.Completed && l.Status != DownloadStatus.Failed);

        var total = await q.CountAsync();

        var logs = await q
            .Include(l => l.DownloadClient)
            .Include(l => l.Files)
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { items = logs.Select(ToResponse), total });
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get download log by ID")]
    [ProducesResponseType(typeof(DownloadLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var log = await db.DownloadLogs
            .Include(l => l.DownloadClient)
            .Include(l => l.Files)
            .FirstOrDefaultAsync(l => l.Id == id);

        return log is null ? NotFound() : Ok(ToResponse(log));
    }

    [HttpPost("{id:guid}/recheck")]
    [EndpointSummary("Recheck a failed download")]
    [EndpointDescription("Resets a failed download log back to Queued, clears the error, and immediately polls the download client. Use this when a download completed while porganizer was offline and was incorrectly marked as Failed.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(DownloadLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recheck(Guid id, CancellationToken ct)
    {
        var log = await db.DownloadLogs
            .Include(l => l.DownloadClient)
            .Include(l => l.Files)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (log is null) return NotFound();

        if (log.Status != DownloadStatus.Failed)
            return BadRequest("Only failed downloads can be rechecked.");

        if (log.ClientItemId is null)
            return BadRequest("Cannot recheck: no download client item ID is recorded for this log entry.");

        log.Status          = porganizer.Database.Enums.DownloadStatus.Queued;
        log.MissedPollCount = 0;
        log.ErrorMessage    = null;
        log.CompletedAt     = null;
        log.CompletionPostProcessedAtUtc = null;
        log.UpdatedAt       = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Poll immediately so the response reflects the actual current state in the client.
        await pollService.PollAsync(ct);

        return Ok(ToResponse(log));
    }

    [HttpPost("{id:guid}/move")]
    [EndpointSummary("Move a completed download's files")]
    [EndpointDescription("Triggers the file-move post-processing step for a single completed download that has not been moved yet. Syncs file records from disk before moving, so downloads that completed while offline are handled correctly. Requires the organize-by-site setting to be enabled with a target folder configured. Returns the updated log and a list of per-step log entries.")]
    [ProducesResponseType(typeof(MoveResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Move(Guid id, CancellationToken ct)
    {
        var log = await db.DownloadLogs
            .Include(l => l.DownloadClient)
            .Include(l => l.Files)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (log is null) return NotFound();

        if (log.Status != DownloadStatus.Completed)
            return BadRequest("Only completed downloads can be moved.");

        if (log.FilesMovedAtUtc is not null)
            return BadRequest("Files have already been moved for this download.");

        var settings = await db.GetSettingsAsync(ct);

        await downloadLogFileSyncService.SyncAsync([id], settings.DeleteNonVideoFilesOnCompletion, ct);

        var entries = await downloadFileMoveService.MoveAsync([id], settings, ct);

        await db.Entry(log).ReloadAsync(ct);

        return Ok(new MoveResponse
        {
            Log     = ToResponse(log),
            Entries = entries.Select(e => new MoveLogEntryResponse { Level = (int)e.Level, Message = e.Message }).ToList(),
        });
    }

    [HttpDelete("failed")]
    [EndpointSummary("Delete failed download logs")]
    [EndpointDescription("Permanently removes all download log entries with a Failed status.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteFailed(CancellationToken ct)
    {
        await db.DownloadLogs
            .Where(l => l.Status == DownloadStatus.Failed)
            .ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpDelete]
    [EndpointSummary("Delete all download logs")]
    [EndpointDescription("Permanently removes all download log entries.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        await db.DownloadLogs.ExecuteDeleteAsync(ct);
        return NoContent();
    }

    private static DownloadLogResponse ToResponse(DownloadLog log) => new()
    {
        Id                   = log.Id,
        IndexerRowId         = log.IndexerRowId,
        DownloadClientId     = log.DownloadClientId,
        DownloadClientTitle  = log.DownloadClient.Title,
        NzbName              = log.NzbName,
        NzbUrl               = log.NzbUrl,
        ClientItemId         = log.ClientItemId,
        Status               = (int)log.Status,
        StoragePath          = log.StoragePath,
        Files                = log.Files.Count > 0
            ? log.Files.Select(f => new DownloadLogFileResponse { Id = f.Id, FileName = f.FileName, OsHash = f.OsHash }).ToList()
            : null,
        TotalSizeBytes       = log.TotalSizeBytes,
        DownloadedBytes      = log.DownloadedBytes,
        ErrorMessage         = log.ErrorMessage,
        LastPolledAt         = log.LastPolledAt,
        CompletedAt          = log.CompletedAt,
        CompletionPostProcessedAtUtc = log.CompletionPostProcessedAtUtc,
        FilesMovedAtUtc      = log.FilesMovedAtUtc,
        CreatedAt            = log.CreatedAt,
        UpdatedAt            = log.UpdatedAt,
    };
}
