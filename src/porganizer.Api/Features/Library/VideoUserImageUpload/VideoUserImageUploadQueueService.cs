using System.Threading.Channels;

namespace porganizer.Api.Features.Library.VideoUserImageUpload;

public class VideoUserImageUploadQueueService
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

    public ChannelReader<Guid> Reader => _channel.Reader;
}
