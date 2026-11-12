using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts.Digest;

public enum DigestOutcomeKind
{
    /// <summary>Email is off or not configured.</summary>
    Disabled,
    /// <summary>The latest send moment was already handled.</summary>
    NotDue,
    /// <summary>Another run, maybe on another server, holds the claim for this period.</summary>
    AlreadyClaimed,
    /// <summary>No new posts, or nobody to send to. The period is closed.</summary>
    Skipped,
    Sent,
    /// <summary>Not one email was accepted. The claim was released and the next run retries the period.</summary>
    Failed,
}

public sealed record DigestOutcome(DigestOutcomeKind Kind, int Posts = 0, int Recipients = 0, int Sent = 0);

/// <summary>Finds the due period, claims it, builds the digest and sends one email per confirmed subscriber.</summary>
public sealed class DigestService(
    IPostSource source,
    ISubscriberRepository subscribers,
    IDigestRunRepository runs,
    IAlertMailer mailer,
    TokenService tokens,
    IOptionsMonitor<PostAlertsOptions> options,
    IOptionsMonitor<GlobalSettings> global,
    TimeProvider time,
    ILogger<DigestService> logger)
{
    private const int PageSize = 500;

    public async Task<DigestOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From).EmailReady
            || !PostAlertsOptionsCheck.TryZone(o.TimeZone, out TimeZoneInfo zone))
        {
            return new DigestOutcome(DigestOutcomeKind.Disabled);
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        DigestRunRow? last = runs.Latest();
        DigestPeriod? due = DigestSchedule.Due(now, last?.PeriodEndUtc, o.SendDay, o.SendHourLocal, zone);
        if (due is not { } period)
        {
            return new DigestOutcome(DigestOutcomeKind.NotDue);
        }

        var run = new DigestRunRow
        {
            Id = Guid.NewGuid(),
            PeriodStartUtc = period.StartUtc,
            PeriodEndUtc = period.EndUtc,
            Status = (int)DigestRunStatus.Started,
            StartedUtc = now,
        };
        if (!runs.TryClaim(run))
        {
            return new DigestOutcome(DigestOutcomeKind.AlreadyClaimed);
        }

        IReadOnlyList<PostCandidate> posts = DigestSelection.Select(source.GetBetween(period.StartUtc, period.EndUtc), period, o.MaxPostsPerDigest);
        List<SubscriberRow> recipients = posts.Count == 0 ? [] : AllConfirmed();
        if (posts.Count == 0 || recipients.Count == 0)
        {
            runs.Complete(run.Id, DigestRunStatus.Skipped, posts.Count, recipients.Count, 0, time.GetUtcNow().UtcDateTime);
            return new DigestOutcome(DigestOutcomeKind.Skipped, posts.Count, recipients.Count);
        }

        int sent = 0;
        try
        {
            IEnumerable<OutgoingMail> mails = recipients.Select(r =>
                Build(o, zone, posts, tokens.UnsubscribeToken(r.Id), r.Email, isTest: false));
            sent = await mailer.SendBatchAsync(mails, "PostAlertsDigest", cancellationToken);
        }
        finally
        {
            if (sent == 0)
            {
                // Nothing was accepted, so closing the week would drop its posts. Release the claim for a retry.
                runs.Complete(run.Id, DigestRunStatus.Failed, posts.Count, recipients.Count, 0, time.GetUtcNow().UtcDateTime);
                runs.Release(run.Id);
            }
            else
            {
                runs.Complete(run.Id, DigestRunStatus.Sent, posts.Count, recipients.Count, sent, time.GetUtcNow().UtcDateTime);
            }
        }
        if (sent == 0)
        {
            logger.LogWarning("Digest for {Start:u} to {End:u}: none of {Recipients} emails were accepted. The next run retries this period.", period.StartUtc, period.EndUtc, recipients.Count);
            return new DigestOutcome(DigestOutcomeKind.Failed, posts.Count, recipients.Count, 0);
        }
        logger.LogInformation("Digest for {Start:u} to {End:u}: {Sent} of {Recipients} sent, {Posts} posts.", period.StartUtc, period.EndUtc, sent, recipients.Count, posts.Count);
        return new DigestOutcome(DigestOutcomeKind.Sent, posts.Count, recipients.Count, sent);
    }

    /// <summary>
    /// Sends one digest to <paramref name="toAddress"/> using the posts of the last period, or the newest posts
    /// when that period had none. Writes no run row and touches no subscriber. Returns how many posts it used.
    /// </summary>
    public async Task<int> SendTestAsync(string toAddress, CancellationToken cancellationToken = default)
    {
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From).EmailReady
            || !PostAlertsOptionsCheck.TryZone(o.TimeZone, out TimeZoneInfo zone))
        {
            throw new InvalidOperationException("Email alerts are not configured.");
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        IReadOnlyList<PostCandidate> all = source.GetAll();
        DateTime end = DigestSchedule.LatestSendMoment(now, o.SendDay, o.SendHourLocal, zone);
        DateTime start = DigestSchedule.SendMoment(TimeZoneInfo.ConvertTimeFromUtc(end, zone).Date.AddDays(-7), o.SendHourLocal, zone);
        IReadOnlyList<PostCandidate> posts = DigestSelection.Select(all, new DigestPeriod(start, now.AddMinutes(1)), o.MaxPostsPerDigest);
        if (posts.Count == 0)
        {
            posts = DigestSelection.Latest(all, Math.Min(3, o.MaxPostsPerDigest));
        }
        if (posts.Count == 0)
        {
            return 0;
        }

        // A token for an id that is not stored: the link works and removes nothing.
        string unsubscribe = tokens.UnsubscribeToken(Guid.NewGuid());
        await mailer.SendAsync(Build(o, zone, posts, unsubscribe, toAddress, isTest: true), "PostAlertsDigestTest", cancellationToken);
        return posts.Count;
    }

    /// <summary>When the next digest is due, or null when email is off.</summary>
    public DateTime? NextDueUtc()
    {
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From).EmailReady
            || !PostAlertsOptionsCheck.TryZone(o.TimeZone, out TimeZoneInfo zone))
        {
            return null;
        }
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateTime? last = runs.Latest()?.PeriodEndUtc;
        // A send moment that passed without a run is due on the next job tick.
        return DigestSchedule.Due(now, last, o.SendDay, o.SendHourLocal, zone) is { } due
            ? due.EndUtc
            : DigestSchedule.NextSendMoment(now, o.SendDay, o.SendHourLocal, zone);
    }

    private OutgoingMail Build(PostAlertsOptions o, TimeZoneInfo zone, IReadOnlyList<PostCandidate> posts, string unsubscribeToken, string to, bool isTest)
    {
        string unsubscribeUrl = AbsoluteUrl.Make(o.SiteBaseUrl, "/baryo-post-alerts/unsubscribe/" + unsubscribeToken)!;
        var model = new DigestEmailModel(SubscriptionService.SiteName(o), AbsoluteUrl.Root(o.SiteBaseUrl) ?? "", posts, unsubscribeUrl, zone, o.SummaryMaxLength, isTest);
        RenderedDigest email = DigestRenderer.Render(model);
        return new OutgoingMail(to, email.Subject, email.Html, IsHtml: true, email.Text, email.Headers);
    }

    private List<SubscriberRow> AllConfirmed()
    {
        var all = new List<SubscriberRow>();
        while (true)
        {
            IReadOnlyList<SubscriberRow> page = subscribers.ListConfirmed(all.Count, PageSize);
            all.AddRange(page);
            if (page.Count < PageSize)
            {
                return all;
            }
        }
    }
}
