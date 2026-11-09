using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Services;
using MimeKit;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class SmtpAlertMailerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 2, 0, 0, TimeSpan.Zero);
    private const string Unsub = "https://example.com/baryo-post-alerts/unsubscribe/TOKEN";

    private static OutgoingMail Digest(string to = "reader@example.com")
    {
        RenderedDigest d = DigestRenderer.Render(new DigestEmailModel("Example", "https://example.com",
            [Posts.Make("Hello world", Posts.Utc(2026, 11, 5))], Unsub, TimeZoneInfo.Utc, 220));
        return new OutgoingMail(to, d.Subject, d.Html, true, d.Text, d.Headers);
    }

    [Fact]
    public void A_digest_is_multipart_alternative_with_text_first_and_html_last()
    {
        MimeMessage m = SmtpAlertMailer.Build(Digest(), "alerts@example.com", "Example", null, Now);

        var alternative = Assert.IsType<MultipartAlternative>(m.Body);
        Assert.Equal(2, alternative.Count);
        var text = Assert.IsType<TextPart>(alternative[0]);
        var html = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(text.IsPlain);
        Assert.True(html.IsHtml);
        Assert.Contains("Hello world", text.Text);
        Assert.Contains("Unsubscribe: " + Unsub, text.Text);
        Assert.Contains("<table", html.Text);
        Assert.Contains(Unsub, html.Text);
    }

    [Fact]
    public void A_digest_carries_the_one_click_unsubscribe_headers()
    {
        MimeMessage m = SmtpAlertMailer.Build(Digest(), "alerts@example.com", "Example", null, Now);

        Assert.Equal("<" + Unsub + ">", m.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", m.Headers["List-Unsubscribe-Post"]);
    }

    [Fact]
    public void A_mailto_is_added_to_list_unsubscribe_only_when_configured()
    {
        MimeMessage m = SmtpAlertMailer.Build(Digest(), "alerts@example.com", "Example", "unsubscribe@example.com", Now);

        Assert.Equal("<" + Unsub + ">, <mailto:unsubscribe@example.com>", m.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", m.Headers["List-Unsubscribe-Post"]);
    }

    [Fact]
    public void A_confirmation_email_is_plain_text_with_no_unsubscribe_headers()
    {
        var mail = new OutgoingMail("reader@example.com", "Confirm", "Open https://example.com/baryo-post-alerts/confirm/abc", IsHtml: false);

        MimeMessage m = SmtpAlertMailer.Build(mail, "alerts@example.com", null, "unsubscribe@example.com", Now);

        var text = Assert.IsType<TextPart>(m.Body);
        Assert.True(text.IsPlain);
        Assert.Null(m.Headers["List-Unsubscribe"]);
        Assert.Null(m.Headers["List-Unsubscribe-Post"]);
    }

    [Fact]
    public void Addresses_subject_date_and_message_id_are_set()
    {
        MimeMessage m = SmtpAlertMailer.Build(Digest("Reader@Example.com"), "alerts@example.com", "Example", null, Now);

        Assert.Equal("alerts@example.com", ((MailboxAddress)m.From[0]).Address);
        Assert.Equal("Example", ((MailboxAddress)m.From[0]).Name);
        Assert.Equal("Reader@Example.com", ((MailboxAddress)m.To[0]).Address);
        Assert.Equal("New post: Hello world", m.Subject);
        Assert.Equal(Now, m.Date);
        Assert.False(string.IsNullOrEmpty(m.MessageId));
    }

    [Fact]
    public void A_line_break_in_the_subject_or_a_header_cannot_add_a_header()
    {
        var mail = new OutgoingMail("reader@example.com", "Hi\r\nBcc: evil@example.com", "x", true, "x",
            new Dictionary<string, string> { ["List-Unsubscribe"] = "<https://example.com/u>\r\nBcc: evil@example.com" });

        MimeMessage m = SmtpAlertMailer.Build(mail, "alerts@example.com", "Ex\r\nBcc: x@y.com", null, Now);
        string raw = m.ToString();

        Assert.Empty(m.Bcc);
        Assert.DoesNotContain("\r\nBcc:", raw);
        Assert.DoesNotContain("\nBcc:", raw);
    }

    [Fact]
    public void The_message_serialises_with_both_parts_and_the_headers()
    {
        string raw = SmtpAlertMailer.Build(Digest(), "alerts@example.com", "Example", null, Now).ToString();

        Assert.Contains("multipart/alternative", raw);
        Assert.Contains("text/plain", raw);
        Assert.Contains("text/html", raw);
        Assert.Contains("List-Unsubscribe: <" + Unsub + ">", raw);
        Assert.Contains("List-Unsubscribe-Post: List-Unsubscribe=One-Click", raw);
    }

    [Theory]
    [InlineData(SecureSocketOptions.None, MailKit.Security.SecureSocketOptions.None)]
    [InlineData(SecureSocketOptions.Auto, MailKit.Security.SecureSocketOptions.Auto)]
    [InlineData(SecureSocketOptions.SslOnConnect, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData(SecureSocketOptions.StartTls, MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData(SecureSocketOptions.StartTlsWhenAvailable, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable)]
    public void Umbraco_socket_options_map_to_mailkit(SecureSocketOptions umbraco, MailKit.Security.SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpAlertMailer.Socket(new SmtpSettings { SecureSocketOptions = umbraco }));
    }

    [Fact]
    public async Task The_default_batch_sends_every_message_and_skips_a_failing_one()
    {
        var mailer = new RecordingMailer();
        mailer.Failing.Add("bad@example.com");
        IAlertMailer asInterface = mailer;

        int sent = await asInterface.SendBatchAsync([Digest("a@example.com"), Digest("bad@example.com"), Digest("c@example.com")], "x");

        Assert.Equal(2, sent);
        Assert.Equal(["a@example.com", "c@example.com"], mailer.Sent.Select(s => s.Mail.To));
    }

    [Fact]
    public void The_new_options_default_to_mailkit_with_no_delay_and_no_mailto()
    {
        var o = new PostAlertsOptions();

        Assert.False(o.UseUmbracoEmailSender);
        Assert.Equal(0, o.SendDelayMilliseconds);
        Assert.Equal("", o.UnsubscribeMailto);
    }
}
