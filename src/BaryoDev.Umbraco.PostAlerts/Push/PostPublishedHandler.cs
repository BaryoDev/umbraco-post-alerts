using BaryoDev.Umbraco.PostAlerts.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>
/// Queues a push the first time a post of a configured type is published. The marker table decides what "first"
/// means, so unpublishing and publishing again, or editing and publishing again, sends nothing more.
/// </summary>
public sealed class PostPublishedHandler(
    IPushQueue queue,
    IPushedPostRepository pushed,
    IOptionsMonitor<PostAlertsOptions> options,
    TimeProvider time,
    ILogger<PostPublishedHandler> logger) : INotificationHandler<ContentPublishedNotification>
{
    public void Handle(ContentPublishedNotification notification)
    {
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o).PushReady)
        {
            return;
        }
        foreach (IContent content in notification.PublishedEntities)
        {
            if (!o.DocumentTypes.Contains(content.ContentType.Alias, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!pushed.TryMark(content.Key, time.GetUtcNow().UtcDateTime))
            {
                continue;
            }
            string? summary = string.IsNullOrWhiteSpace(o.SummaryAlias) ? null : content.GetValue(o.SummaryAlias)?.ToString();
            queue.Enqueue(new PushJob(content.Key, content.Name ?? "", summary));
            logger.LogInformation("Queued a push for the first publish of {Key}.", content.Key);
        }
    }
}
