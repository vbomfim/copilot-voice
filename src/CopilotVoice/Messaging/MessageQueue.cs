using System.Threading.Channels;

namespace CopilotVoice.Messaging;

public class MessageQueue
{
    private readonly Channel<InboundMessage> _channel;

    public MessageQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<InboundMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    public void Enqueue(InboundMessage message)
    {
        _channel.Writer.TryWrite(message);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    public async Task ProcessAsync(Action<InboundMessage> handler, CancellationToken ct)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                handler(message);
            }
            catch
            {
                // Don't let a single message failure stop the queue
            }
        }
    }
}
