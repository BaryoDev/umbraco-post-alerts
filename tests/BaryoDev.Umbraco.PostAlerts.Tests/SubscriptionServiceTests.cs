using System.Text.RegularExpressions;
using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public partial class SubscriptionServiceTests
{
    [GeneratedRegex("/baryo-post-alerts/confirm/([A-Za-z0-9_-]+)")]
    private static partial Regex ConfirmLink();

    private sealed class Rig
    {
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 2, 0, 0, TimeSpan.Zero));
        public FakeSubscribers Subs { get; } = new();
        public RecordingMailer Mailer { get; } = new();
        public PostAlertsOptions Options { get; } = TestOptions.Valid();
        public TokenService Tokens { get; } = new(new EphemeralDataProtectionProvider());
        public SubscriptionService Service { get; }

        public Rig(string? smtpFrom = null)
        {
            Service = new SubscriptionService(Subs, Tokens, Mailer, new RateLimiter(Time), TestOptions.Monitor(Options),
                TestOptions.Global(smtpFrom), Time, NullLogger<SubscriptionService>.Instance);
        }

        public Task<SubscribeResult> Subscribe(string? email, string client = "1.2.3.4", string? honeypot = null) =>
            Service.SubscribeAsync(new SubscribeInput(email, honeypot, client));

        public string LastToken() => ConfirmLink().Match(Mailer.Sent[^1].Mail.Body).Groups[1].Value;
    }

    [Fact]
    public async Task A_new_address_is_stored_pending_and_gets_a_confirmation_email()
    {
        var rig = new Rig();

        SubscribeResult result = await rig.Subscribe("Reader@Example.com ");

        Assert.Equal(SubscribeResult.Accepted, result);
        SubscriberRow row = Assert.Single(rig.Subs.Rows);
        Assert.Equal("reader@example.com", row.Email);
        Assert.Equal((int)SubscriberStatus.Pending, row.Status);
        Assert.Null(row.ConfirmedUtc);
        var (mail, type) = Assert.Single(rig.Mailer.Sent);
        Assert.Equal("PostAlertsConfirm", type);
        Assert.Equal("reader@example.com", mail.To);
        Assert.False(mail.IsHtml);
        Assert.Contains("https://example.com/baryo-post-alerts/confirm/", mail.Body);
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        var rig = new Rig();

        await rig.Subscribe("reader@example.com");

        string token = rig.LastToken();
        SubscriberRow row = rig.Subs.Rows.Single();
        Assert.NotEmpty(token);
        Assert.NotEqual(token, row.TokenHash);
        Assert.Equal(TokenService.Hash(token), row.TokenHash);
        Assert.Equal(64, row.TokenHash!.Length);
        Assert.DoesNotContain(token, row.TokenHash);
    }

    [Fact]
    public async Task The_confirmation_link_confirms_once()
    {
        var rig = new Rig();
        await rig.Subscribe("reader@example.com");
        string token = rig.LastToken();
        rig.Time.Advance(TimeSpan.FromMinutes(30));

        Assert.True(rig.Service.Confirm(token));

        SubscriberRow row = rig.Subs.Rows.Single();
        Assert.Equal((int)SubscriberStatus.Confirmed, row.Status);
        Assert.Null(row.TokenHash);
        Assert.Equal(rig.Time.GetUtcNow().UtcDateTime, row.ConfirmedUtc);
        Assert.False(rig.Service.Confirm(token));
    }

    [Fact]
    public async Task A_wrong_or_empty_token_confirms_nothing()
    {
        var rig = new Rig();
        await rig.Subscribe("reader@example.com");

        Assert.False(rig.Service.Confirm("not-a-token"));
        Assert.False(rig.Service.Confirm(""));
        Assert.False(rig.Service.Confirm(null));
        Assert.False(rig.Service.Confirm(new string('a', 500)));
        Assert.Equal((int)SubscriberStatus.Pending, rig.Subs.Rows.Single().Status);
    }

    [Fact]
    public async Task An_expired_confirmation_does_not_confirm()
    {
        var rig = new Rig();
        await rig.Subscribe("reader@example.com");
        string token = rig.LastToken();
        rig.Time.Advance(TimeSpan.FromHours(73));

        Assert.False(rig.Service.Confirm(token));
        Assert.Equal((int)SubscriberStatus.Pending, rig.Subs.Rows.Single().Status);
    }

    [Fact]
    public async Task Expired_pending_rows_are_purged_and_confirmed_rows_stay()
    {
        var rig = new Rig();
        await rig.Subscribe("old@example.com");
        rig.Subs.AddConfirmed("kept@example.com", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        rig.Time.Advance(TimeSpan.FromHours(80));
        await rig.Subscribe("fresh@example.com", client: "9.9.9.9");

        int removed = rig.Service.PurgeExpired();

        Assert.Equal(1, removed);
        Assert.Equal(["kept@example.com", "fresh@example.com"], rig.Subs.Rows.Select(r => r.Email));
    }

    [Fact]
    public async Task A_confirmed_address_gets_the_same_answer_and_no_email()
    {
        var rig = new Rig();
        rig.Subs.AddConfirmed("reader@example.com");

        SubscribeResult result = await rig.Subscribe("reader@example.com");

        Assert.Equal(SubscribeResult.Accepted, result);
        Assert.Empty(rig.Mailer.Sent);
        Assert.Single(rig.Subs.Rows);
    }

    [Fact]
    public async Task A_pending_address_inside_the_cooldown_gets_no_second_email()
    {
        var rig = new Rig();
        await rig.Subscribe("reader@example.com");
        string first = rig.LastToken();
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        SubscribeResult result = await rig.Subscribe("reader@example.com");

        Assert.Equal(SubscribeResult.Accepted, result);
        Assert.Single(rig.Mailer.Sent);
        Assert.Equal(TokenService.Hash(first), rig.Subs.Rows.Single().TokenHash);
    }

    [Fact]
    public async Task A_pending_address_after_the_cooldown_gets_a_new_token_and_the_old_one_stops_working()
    {
        var rig = new Rig();
        await rig.Subscribe("reader@example.com");
        string first = rig.LastToken();
        rig.Time.Advance(TimeSpan.FromMinutes(6));

        await rig.Subscribe("reader@example.com");

        Assert.Equal(2, rig.Mailer.Sent.Count);
        string second = rig.LastToken();
        Assert.NotEqual(first, second);
        Assert.False(rig.Service.Confirm(first));
        Assert.True(rig.Service.Confirm(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not an email")]
    [InlineData("a@b")]
    [InlineData("a@b.com, c@d.com")]
    [InlineData("a@b.com;c@d.com")]
    [InlineData("Name <a@b.com>")]
    [InlineData("a@b.com\nBcc: x@y.com")]
    public async Task Bad_addresses_are_invalid_and_nothing_is_stored(string? email)
    {
        var rig = new Rig();

        SubscribeResult result = await rig.Subscribe(email);

        Assert.Equal(SubscribeResult.Invalid, result);
        Assert.Empty(rig.Subs.Rows);
        Assert.Empty(rig.Mailer.Sent);
    }

    [Fact]
    public async Task An_address_over_the_length_limit_is_invalid()
    {
        var rig = new Rig();

        SubscribeResult result = await rig.Subscribe(new string('a', 195) + "@example.com");

        Assert.Equal(SubscribeResult.Invalid, result);
    }

    [Fact]
    public async Task A_filled_honeypot_is_accepted_and_ignored()
    {
        var rig = new Rig();

        SubscribeResult result = await rig.Subscribe("bot@example.com", honeypot: "http://spam");

        Assert.Equal(SubscribeResult.Accepted, result);
        Assert.Empty(rig.Subs.Rows);
        Assert.Empty(rig.Mailer.Sent);
    }

    [Fact]
    public async Task The_rate_limit_is_per_client_and_ends_with_the_window()
    {
        var rig = new Rig();
        rig.Options.RateLimitMaxRequests = 2;
        rig.Options.RateLimitWindowMinutes = 10;

        Assert.Equal(SubscribeResult.Accepted, await rig.Subscribe("a@example.com", "5.5.5.5"));
        Assert.Equal(SubscribeResult.Accepted, await rig.Subscribe("b@example.com", "5.5.5.5"));
        Assert.Equal(SubscribeResult.RateLimited, await rig.Subscribe("c@example.com", "5.5.5.5"));
        Assert.Equal(SubscribeResult.Accepted, await rig.Subscribe("c@example.com", "6.6.6.6"));

        rig.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(SubscribeResult.Accepted, await rig.Subscribe("d@example.com", "5.5.5.5"));
        Assert.DoesNotContain(rig.Subs.Rows, r => r.Email == "c@example.com" && r.CreatedUtc != rig.Subs.Rows.First(x => x.Email == "c@example.com").CreatedUtc);
    }

    [Fact]
    public async Task Sign_up_is_unavailable_when_the_options_are_not_ready()
    {
        var rig = new Rig();
        rig.Options.SiteBaseUrl = "";

        Assert.Equal(SubscribeResult.Unavailable, await rig.Subscribe("reader@example.com"));
        Assert.Empty(rig.Subs.Rows);
    }

    [Fact]
    public async Task The_umbraco_smtp_from_address_is_the_fallback_sender()
    {
        var rig = new Rig(smtpFrom: "smtp@example.com");
        rig.Options.FromAddress = "";

        Assert.Equal(SubscribeResult.Accepted, await rig.Subscribe("reader@example.com"));
    }

    [Fact]
    public async Task A_mail_failure_still_gives_the_normal_answer()
    {
        var rig = new Rig();
        rig.Mailer.Failing.Add("reader@example.com");

        SubscribeResult result = await rig.Subscribe("reader@example.com");

        Assert.Equal(SubscribeResult.Accepted, result);
        Assert.Single(rig.Subs.Rows);
    }

    [Fact]
    public void Unsubscribe_deletes_the_row_and_is_safe_to_repeat()
    {
        var rig = new Rig();
        SubscriberRow row = rig.Subs.AddConfirmed("reader@example.com");
        string token = rig.Tokens.UnsubscribeToken(row.Id);

        Assert.True(rig.Service.Unsubscribe(token));
        Assert.Empty(rig.Subs.Rows);
        Assert.True(rig.Service.Unsubscribe(token));
    }

    [Fact]
    public void Unsubscribe_ignores_tokens_that_are_not_ours()
    {
        var rig = new Rig();
        SubscriberRow row = rig.Subs.AddConfirmed("reader@example.com");
        var other = new TokenService(new EphemeralDataProtectionProvider());

        Assert.False(rig.Service.Unsubscribe(other.UnsubscribeToken(row.Id)));
        Assert.False(rig.Service.Unsubscribe(row.Id.ToString()));
        Assert.False(rig.Service.Unsubscribe(""));
        Assert.False(rig.Service.Unsubscribe(null));
        Assert.Single(rig.Subs.Rows);
    }

    [Fact]
    public void A_tampered_unsubscribe_token_does_nothing()
    {
        var rig = new Rig();
        SubscriberRow row = rig.Subs.AddConfirmed("reader@example.com");
        string token = rig.Tokens.UnsubscribeToken(row.Id);
        string tampered = token[..^3] + (token.EndsWith("AAA", StringComparison.Ordinal) ? "BBB" : "AAA");

        Assert.False(rig.Service.Unsubscribe(tampered));
        Assert.Single(rig.Subs.Rows);
    }
}
