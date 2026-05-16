namespace porganizer.Api.Features.DownloadClients;

public sealed class DownloadPollCoordinator
{
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    public async Task<IDisposable> WaitForTurnAsync(CancellationToken ct)
    {
        await _pollLock.WaitAsync(ct);
        return new PollLease(_pollLock);
    }

    private sealed class PollLease(SemaphoreSlim pollLock) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            pollLock.Release();
        }
    }
}
