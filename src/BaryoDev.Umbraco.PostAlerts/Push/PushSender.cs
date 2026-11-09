using BaryoDev.Umbraco.PostAlerts.Data;
using Microsoft.Extensions.Logging;

namespace BaryoDev.Umbraco.PostAlerts.Push;

public sealed record PushReport(int Delivered, int Removed, int Failed);

/// <summary>Sends one payload to every stored subscription and deletes the ones the push service says are gone.</summary>
public sealed class PushSender(IPushSubscriptionRepository subscriptions, IPushTransport transport, ILogger<PushSender> logger)
{
    private const int PageSize = 200;
    private const int MaxParallel = 8;

    public async Task<PushReport> SendToAllAsync(string payload, CancellationToken cancellationToken = default)
    {
        int delivered = 0, failed = 0;
        var gone = new List<Guid>();
        var gate = new object();

        var all = new List<PushSubscriptionRow>();
        while (true)
        {
            IReadOnlyList<PushSubscriptionRow> page = subscriptions.List(all.Count, PageSize);
            all.AddRange(page);
            if (page.Count < PageSize)
            {
                break;
            }
        }

        await Parallel.ForEachAsync(all, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = cancellationToken }, async (row, ct) =>
        {
            PushDelivery result;
            try
            {
                result = await transport.SendAsync(row, payload, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "A push send failed.");
                result = PushDelivery.Failed;
            }
            lock (gate)
            {
                switch (result)
                {
                    case PushDelivery.Delivered: delivered++; break;
                    case PushDelivery.Gone: gone.Add(row.Id); break;
                    default: failed++; break;
                }
            }
        });

        int removed = subscriptions.Delete(gone);
        return new PushReport(delivered, removed, failed);
    }
}
