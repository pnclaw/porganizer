namespace porganizer.Api.Features.DownloadClients;

/// <summary>
/// The outcome of one poll cycle for a single download client.
/// </summary>
/// <param name="ClientReachable">
/// False when the client could not be contacted at all (e.g. connection refused,
/// timeout on the queue request). When false the caller must not advance
/// MissedPollCount — the items may well have completed while porganizer was offline.
/// </param>
/// <param name="Results">
/// Items that were successfully located in the client's queue or history.
/// </param>
/// <param name="HistoryCheckFailedIds">
/// Client item IDs for which the queue was reachable but the individual history
/// lookup threw an exception. The caller must not advance MissedPollCount for
/// these either, because their status is unknown rather than absent.
/// </param>
public record DownloadPollGroupResult(
    bool ClientReachable,
    IReadOnlyList<DownloadPollResult> Results,
    IReadOnlySet<string> HistoryCheckFailedIds)
{
    public static DownloadPollGroupResult ClientUnreachable { get; } =
        new(false, [], new HashSet<string>());
}
