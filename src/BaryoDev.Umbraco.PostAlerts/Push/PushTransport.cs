using System.Net;
using BaryoDev.Umbraco.PostAlerts.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.PostAlerts.Push;

public enum PushDelivery
{
    Delivered,
    /// <summary>The push service answered 404 or 410: the subscription is gone for good.</summary>
    Gone,
    Failed,
}

public interface IPushTransport
{
    Task<PushDelivery> SendAsync(PushSubscriptionRow subscription, string payload, CancellationToken cancellationToken);
}

/// <summary>Web Push with VAPID through Lib.Net.Http.WebPush (MIT).</summary>
public sealed class WebPushTransport(IHttpClientFactory httpClients, IOptionsMonitor<PostAlertsOptions> options, ILogger<WebPushTransport> logger) : IPushTransport
{
    public const string HttpClientName = "BaryoDev.Umbraco.PostAlerts.Push";

    public async Task<PushDelivery> SendAsync(PushSubscriptionRow row, string payload, CancellationToken cancellationToken)
    {
        PostAlertsOptions o = options.CurrentValue;
        var subscription = new PushSubscription { Endpoint = row.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, row.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, row.Auth);

        var message = new PushMessage(payload)
        {
            TimeToLive = Math.Max(1, o.PushTimeToLiveHours) * 3600,
            Urgency = PushMessageUrgency.Normal,
        };
        var vapid = new VapidAuthentication(o.VapidPublicKey.Trim(), o.VapidPrivateKey.Trim()) { Subject = o.VapidSubject.Trim() };

        try
        {
            var client = new PushServiceClient(httpClients.CreateClient(HttpClientName));
            await client.RequestPushMessageDeliveryAsync(subscription, message, vapid, cancellationToken);
            return PushDelivery.Delivered;
        }
        catch (PushServiceClientException e)
        {
            if (Classify(e.StatusCode) == PushDelivery.Gone)
            {
                return PushDelivery.Gone;
            }
            logger.LogWarning("The push service answered {Status} for one subscription.", (int)e.StatusCode);
            return PushDelivery.Failed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Could not reach a push service.");
            return PushDelivery.Failed;
        }
    }

    internal static PushDelivery Classify(HttpStatusCode status) =>
        status is HttpStatusCode.NotFound or HttpStatusCode.Gone ? PushDelivery.Gone : PushDelivery.Failed;
}
