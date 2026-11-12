using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class DigestServiceTests
{
    private sealed class Rig
    {
        // Monday 9 Nov 2026, 10:00 in Manila.
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 2, 0, 0, TimeSpan.Zero));
        public FakePosts Source { get; } = new();
        public FakeSubscribers Subs { get; } = new();
        public FakeRuns Runs { get; } = new();
        public RecordingMailer Mailer { get; } = new();
        public PostAlertsOptions Options { get; } = TestOptions.Valid();
        public TokenService Tokens { get; } = new(new EphemeralDataProtectionProvider());
        public DigestService Service { get; }

        public Rig()
        {
            Service = new DigestService(Source, Subs, Runs, Mailer, Tokens, TestOptions.Monitor(Options), TestOptions.Global(), Time, NullLogger<DigestService>.Instance);
        }
    }

    [Fact]
    public async Task A_week_with_posts_sends_one_email_per_confirmed_subscriber()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        rig.Subs.AddConfirmed("b@example.com");
        rig.Subs.Rows.Add(new SubscriberRow { Id = Guid.NewGuid(), Email = "pending@example.com", Status = (int)SubscriberStatus.Pending });

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(new DigestOutcome(DigestOutcomeKind.Sent, 1, 2, 2), outcome);
        Assert.Equal(["a@example.com", "b@example.com"], rig.Mailer.Sent.Select(s => s.Mail.To));
        Assert.All(rig.Mailer.Sent, s => Assert.Equal("PostAlertsDigest", s.Type));
        DigestRunRow run = Assert.Single(rig.Runs.Rows);
        Assert.Equal((int)DigestRunStatus.Sent, run.Status);
        Assert.Equal(2, run.Sent);
    }

    [Fact]
    public async Task Each_recipient_gets_their_own_working_unsubscribe_link()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        SubscriberRow a = rig.Subs.AddConfirmed("a@example.com");
        SubscriberRow b = rig.Subs.AddConfirmed("b@example.com");

        await rig.Service.RunIfDueAsync();

        foreach (var (row, mail) in new[] { (a, rig.Mailer.Sent[0].Mail), (b, rig.Mailer.Sent[1].Mail) })
        {
            string link = mail.Headers!["List-Unsubscribe"].Trim('<', '>');
            Assert.StartsWith("https://example.com/baryo-post-alerts/unsubscribe/", link);
            string token = link[(link.LastIndexOf('/') + 1)..];
            Assert.Equal(row.Id, rig.Tokens.ReadUnsubscribeToken(token));
            Assert.Contains(link, mail.Body);
            Assert.Contains(link, mail.AlternativeText);
            Assert.True(mail.IsHtml);
        }
    }

    [Fact]
    public async Task A_second_run_for_the_same_week_sends_nothing()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");

        await rig.Service.RunIfDueAsync();
        rig.Time.Advance(TimeSpan.FromMinutes(10));
        DigestOutcome second = await rig.Service.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.NotDue, second.Kind);
        Assert.Single(rig.Mailer.Sent);
        Assert.Single(rig.Runs.Rows);
    }

    [Fact]
    public async Task A_restart_cannot_send_the_same_week_twice()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        await rig.Service.RunIfDueAsync();

        // A new service instance over the same stored runs, like a process restart.
        var restarted = new DigestService(rig.Source, rig.Subs, rig.Runs, rig.Mailer, rig.Tokens, TestOptions.Monitor(rig.Options), TestOptions.Global(), rig.Time, NullLogger<DigestService>.Instance);
        DigestOutcome outcome = await restarted.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.NotDue, outcome.Kind);
        Assert.Single(rig.Mailer.Sent);
    }

    [Fact]
    public async Task A_run_claimed_elsewhere_is_not_repeated()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        // Another server claimed the period but the latest lookup has not seen it yet.
        var racing = new RacingRuns(rig.Runs);
        var service = new DigestService(rig.Source, rig.Subs, racing, rig.Mailer, rig.Tokens, TestOptions.Monitor(rig.Options), TestOptions.Global(), rig.Time, NullLogger<DigestService>.Instance);

        DigestOutcome outcome = await service.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.AlreadyClaimed, outcome.Kind);
        Assert.Empty(rig.Mailer.Sent);
    }

    private sealed class RacingRuns(FakeRuns inner) : IDigestRunRepository
    {
        public DigestRunRow? Latest() => null;

        public bool TryClaim(DigestRunRow row)
        {
            inner.TryClaim(new DigestRunRow { Id = Guid.NewGuid(), PeriodEndUtc = row.PeriodEndUtc, PeriodStartUtc = row.PeriodStartUtc });
            return inner.TryClaim(row);
        }

        public void Complete(Guid id, DigestRunStatus status, int postCount, int recipients, int sent, DateTime completedUtc) =>
            inner.Complete(id, status, postCount, recipients, sent, completedUtc);

        public void Release(Guid id) => inner.Release(id);
    }

    [Fact]
    public async Task A_week_with_no_new_posts_sends_nothing_and_closes_the_period()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("Old", Posts.Utc(2026, 10, 1)));
        rig.Subs.AddConfirmed("a@example.com");

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.Skipped, outcome.Kind);
        Assert.Empty(rig.Mailer.Sent);
        Assert.Equal((int)DigestRunStatus.Skipped, Assert.Single(rig.Runs.Rows).Status);
        Assert.Equal(DigestOutcomeKind.NotDue, (await rig.Service.RunIfDueAsync()).Kind);
    }

    [Fact]
    public async Task No_subscribers_means_a_skipped_run()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.Skipped, outcome.Kind);
        Assert.Empty(rig.Mailer.Sent);
    }

    [Fact]
    public async Task The_next_week_only_carries_posts_since_the_last_run()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("Week one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        await rig.Service.RunIfDueAsync();
        rig.Source.Posts.Add(Posts.Make("Week two", Posts.Utc(2026, 11, 12)));
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.Zero));

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(DigestOutcomeKind.Sent, outcome.Kind);
        Assert.Equal(1, outcome.Posts);
        Assert.Contains("Week two", rig.Mailer.Sent[^1].Mail.Body);
        Assert.DoesNotContain("Week one", rig.Mailer.Sent[^1].Mail.Body);
    }

    [Fact]
    public async Task A_missed_week_is_caught_up_once_with_both_weeks_of_posts()
    {
        var rig = new Rig();
        rig.Subs.AddConfirmed("a@example.com");
        rig.Runs.Rows.Add(new DigestRunRow { Id = Guid.NewGuid(), PeriodStartUtc = Posts.Utc(2026, 10, 19, 1), PeriodEndUtc = Posts.Utc(2026, 10, 26, 1), Status = (int)DigestRunStatus.Sent });
        rig.Source.Posts.Add(Posts.Make("Missed week", Posts.Utc(2026, 11, 3)));
        rig.Source.Posts.Add(Posts.Make("Latest week", Posts.Utc(2026, 11, 7)));
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 11, 5, 0, 0, TimeSpan.Zero));

        DigestOutcome first = await rig.Service.RunIfDueAsync();
        DigestOutcome again = await rig.Service.RunIfDueAsync();

        Assert.Equal(2, first.Posts);
        Assert.Equal(DigestOutcomeKind.NotDue, again.Kind);
        Assert.Single(rig.Mailer.Sent);
        Assert.Equal(2, rig.Runs.Rows.Count);
    }

    [Fact]
    public async Task One_failing_address_does_not_stop_the_others()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        rig.Subs.AddConfirmed("bad@example.com");
        rig.Subs.AddConfirmed("c@example.com");
        rig.Mailer.Failing.Add("bad@example.com");

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(3, outcome.Recipients);
        Assert.Equal(2, outcome.Sent);
        Assert.Equal(2, rig.Runs.Rows.Single().Sent);
        Assert.Equal((int)DigestRunStatus.Sent, rig.Runs.Rows.Single().Status);
    }

    [Fact]
    public async Task A_run_where_every_send_fails_is_released_and_retried_on_the_next_tick()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");
        rig.Subs.AddConfirmed("b@example.com");
        rig.Mailer.Failing.Add("a@example.com");
        rig.Mailer.Failing.Add("b@example.com");

        DigestOutcome first = await rig.Service.RunIfDueAsync();

        Assert.Equal(new DigestOutcome(DigestOutcomeKind.Failed, 1, 2, 0), first);
        Assert.Empty(rig.Runs.Rows);

        rig.Mailer.Failing.Clear();
        rig.Time.Advance(TimeSpan.FromMinutes(10));
        DigestOutcome retry = await rig.Service.RunIfDueAsync();

        Assert.Equal(new DigestOutcome(DigestOutcomeKind.Sent, 1, 2, 2), retry);
        Assert.Equal(["a@example.com", "b@example.com"], rig.Mailer.Sent.Select(s => s.Mail.To));
        Assert.Equal((int)DigestRunStatus.Sent, Assert.Single(rig.Runs.Rows).Status);
    }

    [Fact]
    public async Task The_digest_asks_the_source_for_the_period_only()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Source.Posts.Add(Posts.Make("Old one", Posts.Utc(2026, 5, 5)));
        rig.Subs.AddConfirmed("a@example.com");

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(1, outcome.Posts);
        (DateTime start, DateTime end) = Assert.Single(rig.Source.Asked);
        Assert.Equal(Posts.Utc(2026, 11, 9, 1), end);
        Assert.True(start > Posts.Utc(2026, 10, 1));
    }

    [Fact]
    public async Task Nothing_runs_when_email_is_not_configured()
    {
        var rig = new Rig();
        rig.Options.SiteBaseUrl = "";
        rig.Source.Posts.Add(Posts.Make("New one", Posts.Utc(2026, 11, 5)));
        rig.Subs.AddConfirmed("a@example.com");

        Assert.Equal(DigestOutcomeKind.Disabled, (await rig.Service.RunIfDueAsync()).Kind);
        Assert.Empty(rig.Runs.Rows);
        Assert.Null(rig.Service.NextDueUtc());
    }

    [Fact]
    public async Task The_cap_limits_posts_in_one_email()
    {
        var rig = new Rig();
        rig.Options.MaxPostsPerDigest = 2;
        for (int i = 1; i <= 4; i++)
        {
            rig.Source.Posts.Add(Posts.Make("Post " + i, Posts.Utc(2026, 11, 2, 1 + i)));
        }
        rig.Subs.AddConfirmed("a@example.com");

        DigestOutcome outcome = await rig.Service.RunIfDueAsync();

        Assert.Equal(2, outcome.Posts);
    }

    [Fact]
    public async Task Next_due_is_the_following_send_moment_once_this_week_has_run()
    {
        var rig = new Rig();
        Assert.Equal(Posts.Utc(2026, 11, 9, 1), rig.Service.NextDueUtc());

        rig.Subs.AddConfirmed("a@example.com");
        await rig.Service.RunIfDueAsync();

        Assert.Equal(Posts.Utc(2026, 11, 16, 1), rig.Service.NextDueUtc());
    }

    [Fact]
    public async Task The_test_digest_goes_to_one_address_and_writes_no_run()
    {
        var rig = new Rig();
        rig.Source.Posts.Add(Posts.Make("Old post", Posts.Utc(2026, 9, 1)));
        rig.Subs.AddConfirmed("a@example.com");

        int posts = await rig.Service.SendTestAsync("me@example.com");

        Assert.Equal(1, posts);
        var (mail, type) = Assert.Single(rig.Mailer.Sent);
        Assert.Equal("me@example.com", mail.To);
        Assert.Equal("PostAlertsDigestTest", type);
        Assert.StartsWith("[Test] ", mail.Subject);
        Assert.Empty(rig.Runs.Rows);
    }

    [Fact]
    public async Task The_test_digest_with_no_posts_sends_nothing()
    {
        var rig = new Rig();

        Assert.Equal(0, await rig.Service.SendTestAsync("me@example.com"));
        Assert.Empty(rig.Mailer.Sent);
    }

    [Fact]
    public async Task The_test_digest_needs_working_options()
    {
        var rig = new Rig();
        rig.Options.FromAddress = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Service.SendTestAsync("me@example.com"));
    }
}
