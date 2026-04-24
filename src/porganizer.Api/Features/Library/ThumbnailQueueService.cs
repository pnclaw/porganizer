using System.Threading.Channels;

namespace porganizer.Api.Features.Library;

public class ThumbnailQueueService
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    public void Enqueue(Guid fileId)
    {
        _channel.Writer.TryWrite(fileId);
    }

    public void EnqueueMany(IEnumerable<Guid> fileIds)
    {
        foreach (var id in fileIds)
            _channel.Writer.TryWrite(id);
    }

    public ChannelReader<Guid> Reader => _channel.Reader;
}
