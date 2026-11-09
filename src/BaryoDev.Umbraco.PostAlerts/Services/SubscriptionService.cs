using System.Globalization;
using BaryoDev.Umbraco.PostAlerts.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts.Services;

public enum SubscribeResult
{
    /// <summary>The request was taken. It does not say whether an email was sent.</summary>
    Accepted,
    Invalid,
    RateLimited,
    /// <summary>Email sign-up is switched off or not configured.</summary>
    Unavailable,
}

public sealed record SubscribeInput(string? Email, string? Honeypot, string Client);

/// <summary>Double opt-in sign-up, confirmation and removal. It never says whether an address is already subscribed.</summary>
public sealed class SubscriptionService(
    ISubscriberRepository subscribers,
    TokenService tokens,
    IAlertMailer mailer,
    RateLimiter limiter,
    IOptionsMonitor<PostAlertsOptions> options,
    IOptionsMonitor<GlobalSettings> global,
    TimeProvider time,
    ILogger<SubscriptionService> logger)
{
    public async Task<SubscribeResult> SubscribeAsync(SubscribeInput input, CancellationToken cancellationToken = default)
    {
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From).EmailReady)
        {
            return SubscribeResult.Unavailable;
        }

        // A bot fills the hidden field. It gets the same answer as a person, so it learns nothing.
        if (!string.IsNullOrWhiteSpace(input.Honeypot))
        {
            return SubscribeResult.Accepted;
        }

        if (!limiter.TryAcquire(input.Client, o.RateLimitMaxRequests, TimeSpan.FromMinutes(o.RateLimitWindowMinutes)))
        {
            return SubscribeResult.RateLimited;
        }

        string? email = NormalizeEmail(input.Email);
        if (email is null)
        {
            return SubscribeResult.Invalid;
        }

        DateTime now = time.GetUtcNow().UtcDateTime;
        SubscriberRow? existing = subscribers.GetByEmail(email);

        if (existing is { Status: (int)SubscriberStatus.Confirmed })
        {
            return SubscribeResult.Accepted;
        }

        string token = tokens.NewConfirmationToken();
        if (existing is null)
        {
            existing = new SubscriberRow
            {
                Id = Guid.NewGuid(),
                Email = email,
                Status = (int)SubscriberStatus.Pending,
                TokenHash = TokenService.Hash(token),
                CreatedUtc = now,
            };
            try
            {
                subscribers.Add(existing);
            }
            catch (Exception) when (subscribers.GetByEmail(email) is not null)
            {
                // Two requests for the same new address at once. The other one sends the email.
                return SubscribeResult.Accepted;
            }
        }
        else
        {
            if (now - existing.CreatedUtc < TimeSpan.FromMinutes(Math.Max(0, o.ResendCooldownMinutes)))
            {
                return SubscribeResult.Accepted;
            }
            existing.TokenHash = TokenService.Hash(token);
            existing.CreatedUtc = now;
            subscribers.Update(existing);
        }

        try
        {
            await mailer.SendAsync(ConfirmationMail(o, email, token), "PostAlertsConfirm", cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The visitor sees the same message either way. The log says why no email came.
            logger.LogWarning(e, "Could not send the confirmation email.");
        }
        return SubscribeResult.Accepted;
    }

    /// <summary>Confirms the address behind a token. False when the token is unknown, used or expired.</summary>
    public bool Confirm(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return false;
        }
        SubscriberRow? row = subscribers.GetByTokenHash(TokenService.Hash(token.Trim()));
        if (row is null)
        {
            return false;
        }
        DateTime now = time.GetUtcNow().UtcDateTime;
        if (row.Status != (int)SubscriberStatus.Pending || now - row.CreatedUtc > TimeSpan.FromHours(Math.Max(1, options.CurrentValue.ConfirmationHours)))
        {
            return false;
        }
        row.Status = (int)SubscriberStatus.Confirmed;
        row.TokenHash = null;
        row.ConfirmedUtc = now;
        subscribers.Update(row);
        return true;
    }

    /// <summary>
    /// Deletes the subscriber behind an unsubscribe token. True when the token is genuine, even if the row is
    /// already gone, so repeating the request is harmless. False when the token is not ours.
    /// </summary>
    public bool Unsubscribe(string? token)
    {
        Guid? id = tokens.ReadUnsubscribeToken(token);
        if (id is null)
        {
            return false;
        }
        subscribers.Delete(id.Value);
        return true;
    }

    /// <summary>Removes pending requests nobody confirmed in time.</summary>
    public int PurgeExpired()
    {
        DateTime cutoff = time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(Math.Max(1, options.CurrentValue.ConfirmationHours));
        return subscribers.DeletePendingBefore(cutoff);
    }

    internal static string? NormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        string value = TextCleaner.Cut(raw.Trim(), 1000);
        if (value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains(',') || value.Contains(';'))
        {
            return null;
        }
        value = value.ToLowerInvariant();
        return value.Length <= SubscriberRow.EmailLength && PostAlertsOptionsCheck.IsPlainAddress(value) ? value : null;
    }

    private OutgoingMail ConfirmationMail(PostAlertsOptions o, string email, string token)
    {
        string site = SiteName(o);
        string link = AbsoluteUrl.Make(o.SiteBaseUrl, "/baryo-post-alerts/confirm/" + Uri.EscapeDataString(token))!;
        string hours = Math.Max(1, o.ConfirmationHours).ToString(CultureInfo.InvariantCulture);
        string body =
            $"Someone asked to get new post alerts from {site} at this address.\n\n" +
            $"To confirm, open this link:\n{link}\n\n" +
            $"The link works for {hours} hours. If you did not ask for this, ignore this email and nothing will be sent.\n";
        return new OutgoingMail(email, $"Confirm your subscription to {site}", body, IsHtml: false, Expires: TimeSpan.FromHours(Math.Max(1, o.ConfirmationHours)));
    }

    internal static string SiteName(PostAlertsOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.FromName))
        {
            return o.FromName.Trim();
        }
        return PostAlertsOptionsCheck.TryBaseUrl(o.SiteBaseUrl, out Uri root) ? root.Host : "this site";
    }
}
