using System.Net.Mail;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>One email. Headers and AlternativeText are for mailers that can carry them.</summary>
public sealed record OutgoingMail(
    string To,
    string Subject,
    string Body,
    bool IsHtml,
    string? AlternativeText = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    TimeSpan? Expires = null);

/// <summary>
/// Sends the package's email. The default sends through Umbraco's own <see cref="IEmailSender"/>, which has no
/// way to set custom headers or a second body part. A site that needs List-Unsubscribe headers or multipart mail
/// can register its own implementation of this interface.
/// </summary>
public interface IAlertMailer
{
    Task SendAsync(OutgoingMail mail, string emailType, CancellationToken cancellationToken = default);
}

public sealed class UmbracoAlertMailer(
    IEmailSender sender,
    IOptionsMonitor<PostAlertsOptions> options,
    IOptionsMonitor<GlobalSettings> global) : IAlertMailer
{
    public Task SendAsync(OutgoingMail mail, string emailType, CancellationToken cancellationToken = default)
    {
        PostAlertsOptions o = options.CurrentValue;
        string address = PostAlertsOptionsCheck.EffectiveFrom(o, global.CurrentValue.Smtp?.From)
            ?? throw new InvalidOperationException("No From address. Set BaryoDev:PostAlerts:FromAddress or Umbraco:CMS:Global:Smtp:From.");
        string from = string.IsNullOrWhiteSpace(o.FromName) ? address : new MailAddress(address, o.FromName.Trim()).ToString();

        // Umbraco's EmailMessage carries one body and no custom headers, so Headers and AlternativeText are dropped here.
        var message = new EmailMessage(from, mail.To, mail.Subject, mail.Body, mail.IsHtml);
        return sender.SendAsync(message, emailType, false, mail.Expires ?? TimeSpan.FromHours(24));
    }
}
