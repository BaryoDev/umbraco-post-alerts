using System.Threading.Channels;

namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>A post whose first publish needs a push. Title and summary are read at publish time.</summary>
public sealed record PushJob(Guid Key, string Title, string? Summary);

/// <summary>Hands jobs from the publish notification to the background sender, so publishing never waits on push services.</summary>
public interface IPushQueue
{
    void Enqueue(PushJob job);
}

public sealed class PushQueue : IPushQueue
{
    private readonly Channel<PushJob> _channel = Channel.CreateUnbounded<PushJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(PushJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<PushJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
