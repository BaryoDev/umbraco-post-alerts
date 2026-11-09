using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>
/// Default mailer. Sends with MailKit using Umbraco's own SMTP settings (<c>Umbraco:CMS:Global:Smtp</c>), so a site
/// configures SMTP once. Digest mail goes out as multipart/alternative with List-Unsubscribe and
/// List-Unsubscribe-Post headers. It falls back to <see cref="UmbracoAlertMailer"/> when
/// <c>UseUmbracoEmailSender</c> is true, when no SMTP host is set, or when Umbraco uses a pickup directory.
/// </summary>
public sealed class SmtpAlertMailer : IAlertMailer
{
    private readonly UmbracoAlertMailer fallback;
    private readonly IOptionsMonitor<PostAlertsOptions> options;
    private readonly IOptionsMonitor<GlobalSettings> global;
    private readonly TimeProvider time;
    private readonly ILogger<SmtpAlertMailer> logger;

    public SmtpAlertMailer(
        UmbracoAlertMailer fallback,
        IOptionsMonitor<PostAlertsOptions> options,
        IOptionsMonitor<GlobalSettings> global,
        TimeProvider time,
        ILogger<SmtpAlertMailer> logger)
    {
        this.fallback = fallback;
        this.options = options;
        this.global = global;
        this.time = time;
        this.logger = logger;
    }

    public async Task SendAsync(OutgoingMail mail, string emailType, CancellationToken cancellationToken = default)
    {
        if (UseFallback())
        {
            await fallback.SendAsync(mail, emailType, cancellationToken);
            return;
        }
        // One message must fail loudly so the caller can log it.
        await using var session = new Session(this);
        await session.SendAsync(mail, throwOnError: true, cancellationToken);
    }

    public async Task<int> SendBatchAsync(IEnumerable<OutgoingMail> mails, string emailType, CancellationToken cancellationToken = default)
    {
        if (UseFallback())
        {
            int count = 0;
            foreach (OutgoingMail mail in mails)
            {
                try
                {
                    await fallback.SendAsync(mail, emailType, cancellationToken);
                    count++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Could not send one message.");
                }
            }
            return count;
        }

        int sent = 0;
        int delay = Math.Max(0, options.CurrentValue.SendDelayMilliseconds);
        await using var session = new Session(this);
        foreach (OutgoingMail mail in mails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await session.SendAsync(mail, throwOnError: false, cancellationToken))
            {
                sent++;
            }
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
        return sent;
    }

    private bool UseFallback()
    {
        SmtpSettings? smtp = global.CurrentValue.Smtp;
        return options.CurrentValue.UseUmbracoEmailSender
            || smtp is null
            || string.IsNullOrWhiteSpace(smtp.Host)
            || smtp.DeliveryMethod != System.Net.Mail.SmtpDeliveryMethod.Network;
    }

    /// <summary>Builds the message. Public to the tests so the MIME can be checked without a server.</summary>
    internal static MimeMessage Build(OutgoingMail mail, string fromAddress, string? fromName, string? unsubscribeMailto, DateTimeOffset now)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(fromName) ? null : OneLine(fromName), fromAddress));
        message.To.Add(MailboxAddress.Parse(mail.To));
        message.Subject = OneLine(mail.Subject);
        message.Date = now;
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();

        if (mail.IsHtml)
        {
            var body = new BodyBuilder { HtmlBody = mail.Body, TextBody = mail.AlternativeText ?? "" };
            message.Body = body.ToMessageBody();
        }
        else
        {
            message.Body = new TextPart("plain") { Text = mail.Body };
        }

        foreach (var (name, value) in mail.Headers ?? new Dictionary<string, string>())
        {
            string line = OneLine(value);
            if (name.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(unsubscribeMailto))
            {
                line += ", <mailto:" + OneLine(unsubscribeMailto.Trim()) + ">";
            }
            message.Headers.Add(OneLine(name), line);
        }
        return message;
    }

    /// <summary>Umbraco's setting uses the same names as MailKit's. An unknown value means Auto.</summary>
    internal static MailKit.Security.SecureSocketOptions Socket(SmtpSettings smtp) =>
        Enum.TryParse(smtp.SecureSocketOptions.ToString(), ignoreCase: true, out MailKit.Security.SecureSocketOptions parsed)
            ? parsed
            : MailKit.Security.SecureSocketOptions.Auto;

    private static string OneLine(string value) => value.Replace("\r", "").Replace("\n", " ");

    private sealed class Session(SmtpAlertMailer owner) : IAsyncDisposable
    {
        private SmtpClient? _client;

        public async Task<bool> SendAsync(OutgoingMail mail, bool throwOnError, CancellationToken cancellationToken)
        {
            PostAlertsOptions o = owner.options.CurrentValue;
            try
            {
                string address = PostAlertsOptionsCheck.EffectiveFrom(o, owner.global.CurrentValue.Smtp?.From)
                    ?? throw new InvalidOperationException("No From address. Set BaryoDev:PostAlerts:FromAddress or Umbraco:CMS:Global:Smtp:From.");
                MimeMessage message = Build(mail, address, o.FromName, o.UnsubscribeMailto, owner.time.GetUtcNow());
                _client = await ConnectedAsync(cancellationToken);
                await _client.SendAsync(message, cancellationToken);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException && !throwOnError)
            {
                owner.logger.LogWarning(e, "Could not send one message.");
                // A dropped connection is rebuilt for the next message.
                if (_client is { IsConnected: false })
                {
                    _client.Dispose();
                    _client = null;
                }
                return false;
            }
        }

        private async Task<SmtpClient> ConnectedAsync(CancellationToken cancellationToken)
        {
            if (_client is { IsConnected: true })
            {
                return _client;
            }
            _client?.Dispose();
            SmtpSettings smtp = owner.global.CurrentValue.Smtp!;
            var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(smtp.Host ?? "", smtp.Port, Socket(smtp), cancellationToken);
                if (!string.IsNullOrWhiteSpace(smtp.Username))
                {
                    await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", cancellationToken);
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }
            _client = client;
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            if (_client is null)
            {
                return;
            }
            try
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(true);
                }
            }
            catch (Exception e)
            {
                owner.logger.LogDebug(e, "Closing the SMTP connection failed.");
            }
            _client.Dispose();
        }
    }
}
