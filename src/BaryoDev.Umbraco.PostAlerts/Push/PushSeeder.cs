using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>
/// On the first start with push configured, marks every post that is already published, so republishing an old
/// post after installing the package does not send a push. It does nothing once any post is marked.
/// </summary>
public sealed class PushSeeder(
    IPostSource source,
    IPushedPostRepository pushed,
    IOptionsMonitor<PostAlertsOptions> options,
    TimeProvider time,
    ILogger<PushSeeder> logger) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        if (!PostAlertsOptionsCheck.Check(options.CurrentValue).PushReady || pushed.Count() > 0)
        {
            return;
        }
        try
        {
            DateTime now = time.GetUtcNow().UtcDateTime;
            int marked = 0;
            foreach (Guid key in source.GetAllKeys())
            {
                if (pushed.TryMark(key, now))
                {
                    marked++;
                }
            }
            logger.LogInformation("Marked {Count} existing posts as already announced.", marked);
        }
        catch (Exception e)
        {
            // The site must start even if the tables are not there yet.
            logger.LogWarning(e, "Could not mark existing posts.");
        }
    }
}
