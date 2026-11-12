using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>
/// Sends queued pushes. The published cache is refreshed just after a publish, so the post's address is looked up
/// after a short wait and retried a few times before the push is dropped.
/// </summary>
public sealed class PushDispatchService(
    PushQueue queue,
    IPostSource source,
    PushSender sender,
    IPushedPostRepository pushed,
    IOptionsMonitor<PostAlertsOptions> options,
    ILogger<PushDispatchService> logger) : BackgroundService
{
    internal TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(3);
    internal int Attempts { get; init; } = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (PushJob job in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DispatchAsync(job, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Could not send the push for {Key}.", job.Key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    internal async Task<PushReport?> DispatchAsync(PushJob job, CancellationToken cancellationToken)
    {
        PostAlertsOptions o = options.CurrentValue;
        PostCandidate? post = null;
        for (int attempt = 0; attempt < Attempts && post?.Url is null; attempt++)
        {
            if (Wait > TimeSpan.Zero)
            {
                await Task.Delay(Wait, cancellationToken);
            }
            post = source.Get(job.Key);
        }
        if (post?.Url is null)
        {
            logger.LogWarning("No published address for {Key}, so no push was sent. It stays queued and is tried again at the next start.", job.Key);
            return null;
        }

        string payload = PushPayload.Build(job.Key, string.IsNullOrWhiteSpace(job.Title) ? post.Title : job.Title, job.Summary ?? post.Summary, post.Url);
        PushReport report = await sender.SendToAllAsync(payload, cancellationToken);
        pushed.MarkSent(job.Key);
        logger.LogInformation("Push for {Key}: {Delivered} delivered, {Removed} removed, {Failed} failed.", job.Key, report.Delivered, report.Removed, report.Failed);
        return report;
    }
}
