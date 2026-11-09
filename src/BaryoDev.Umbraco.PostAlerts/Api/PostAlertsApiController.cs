using Asp.Versioning;
using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.PostAlerts.Api;

public sealed record LastDigest(DateTime PeriodEndUtc, string Status, int Posts, int Recipients, int Sent, DateTime? CompletedUtc);

public sealed record AlertsSummary(
    int ConfirmedSubscribers,
    int PendingSubscribers,
    int PushSubscriptions,
    bool EmailReady,
    IReadOnlyList<string> EmailProblems,
    bool PushReady,
    IReadOnlyList<string> PushProblems,
    LastDigest? LastDigest,
    DateTime? NextDigestUtc,
    string TimeZone);

public sealed record TestDigestResult(int Posts, string SentTo);

/// <summary>Dashboard data and the test digest. Content section access, checked on the server. No subscriber address leaves this controller.</summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("post-alerts")]
[ApiExplorerSettings(GroupName = "Post alerts")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
public sealed class PostAlertsApiController(
    ISubscriberRepository subscribers,
    IPushSubscriptionRepository pushSubscriptions,
    IDigestRunRepository runs,
    DigestService digest,
    IBackOfficeSecurityAccessor security,
    IOptionsMonitor<PostAlertsOptions> options,
    IOptionsMonitor<GlobalSettings> global) : ManagementApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<AlertsSummary>(StatusCodes.Status200OK)]
    public IActionResult Summary()
    {
        PostAlertsOptions o = options.CurrentValue;
        PostAlertsOptionsCheck.Result check = PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From);
        SubscriberCounts counts = subscribers.Counts();
        DigestRunRow? last = runs.Latest();
        return Ok(new AlertsSummary(
            counts.Confirmed,
            counts.Pending,
            pushSubscriptions.Count(),
            check.EmailReady,
            check.Email,
            check.PushReady,
            check.Push,
            last is null ? null : new LastDigest(
                DateTime.SpecifyKind(last.PeriodEndUtc, DateTimeKind.Utc),
                ((DigestRunStatus)last.Status).ToString().ToLowerInvariant(),
                last.PostCount, last.Recipients, last.Sent,
                last.CompletedUtc is { } done ? DateTime.SpecifyKind(done, DateTimeKind.Utc) : null),
            digest.NextDueUtc(),
            o.TimeZone));
    }

    /// <summary>Sends one digest to the signed-in user's own address. It writes no run row and does not touch subscribers.</summary>
    [HttpPost("test-digest")]
    [ProducesResponseType<TestDigestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestDigest(CancellationToken cancellationToken)
    {
        string? email = security.BackOfficeSecurity?.CurrentUser?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "Your backoffice account has no email address." });
        }
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From).EmailReady)
        {
            return BadRequest(new { error = "Email alerts are not configured. See the problems on this dashboard." });
        }
        try
        {
            int posts = await digest.SendTestAsync(email, cancellationToken);
            return posts == 0
                ? BadRequest(new { error = "No published posts of the configured types were found." })
                : Ok(new TestDigestResult(posts, email));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return BadRequest(new { error = "The mail server refused the message: " + e.Message });
        }
    }
}
