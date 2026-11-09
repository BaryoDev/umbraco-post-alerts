using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace BaryoDev.Umbraco.PostAlerts.Digest;

/// <summary>
/// Wakes every ten minutes, sends the digest when a send moment has passed without a run, and removes
/// confirmation requests nobody answered. Runs on the scheduling publisher only.
/// </summary>
public sealed class DigestJob(DigestService digest, SubscriptionService subscriptions, ILogger<DigestJob> logger) : IRecurringBackgroundJob
{
    public TimeSpan Period => TimeSpan.FromMinutes(10);

    public TimeSpan Delay => TimeSpan.FromMinutes(1);

    public ServerRole[] ServerRoles => [ServerRole.Single, ServerRole.SchedulingPublisher];

    public event EventHandler PeriodChanged { add { } remove { } }

    public async Task RunJobAsync()
    {
        try
        {
            subscriptions.PurgeExpired();
            await digest.RunIfDueAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "The digest job failed.");
        }
    }
}
