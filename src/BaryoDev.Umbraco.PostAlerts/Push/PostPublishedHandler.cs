using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Services;
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
    IProtectionCheck protection,
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
            string? summary = string.IsNullOrWhiteSpace(o.SummaryAlias) ? null : content.GetValue(o.SummaryAlias)?.ToString();
            Consider(o, content.Key, content.ContentType.Alias, content.Path, content.Name ?? "", summary);
        }
    }

    internal bool Consider(PostAlertsOptions o, Guid key, string alias, string path, string name, string? summary)
    {
        if (!o.DocumentTypes.Contains(alias, StringComparer.OrdinalIgnoreCase) || protection.IsExcluded(path))
        {
            return false;
        }
        if (!pushed.TryMark(key, time.GetUtcNow().UtcDateTime))
        {
            return false;
        }
        queue.Enqueue(new PushJob(key, name, summary));
        logger.LogInformation("Queued a push for the first publish of {Key}.", key);
        return true;
    }
}
