using System.Net;
using System.Reflection;
using System.Text.Json;
using BaryoDev.Umbraco.PostAlerts.Api;
using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Push;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class PushTests
{
    private static readonly string[] Hosts = ["fcm.googleapis.com", "push.services.mozilla.com", "notify.windows.com", "push.apple.com"];

    private sealed class FakeProtection(params string[] excluded) : IProtectionCheck
    {
        public bool IsExcluded(string path) => excluded.Contains(path);
    }

    private sealed class Queue : IPushQueue
    {
        public List<PushJob> Jobs { get; } = [];

        public void Enqueue(PushJob job) => Jobs.Add(job);
    }

    private static (PostPublishedHandler Handler, Queue Queue, FakePushed Pushed) Handler(IProtectionCheck? protection = null)
    {
        var queue = new Queue();
        var pushed = new FakePushed();
        var handler = new PostPublishedHandler(queue, pushed, protection ?? new FakeProtection(), TestOptions.Monitor(TestOptions.WithPush()),
            new FakeTimeProvider(), NullLogger<PostPublishedHandler>.Instance);
        return (handler, queue, pushed);
    }

    [Fact]
    public void The_payload_carries_title_summary_url_and_a_tag()
    {
        Guid key = Guid.NewGuid();

        using JsonDocument doc = JsonDocument.Parse(PushPayload.Build(key, "Hello", "<p>About it</p>", "https://example.com/hello/"));

        Assert.Equal("Hello", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("About it", doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("https://example.com/hello/", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("post-" + key.ToString("N"), doc.RootElement.GetProperty("tag").GetString());
    }

    [Fact]
    public void Long_text_is_cut_and_the_payload_stays_small()
    {
        string json = PushPayload.Build(Guid.NewGuid(), new string('t', 500), new string('s', 5000), "https://example.com/x/");

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("title").GetString()!.Length <= PushPayload.MaxTitle + 3);
        Assert.True(doc.RootElement.GetProperty("body").GetString()!.Length <= PushPayload.MaxBody + 3);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < PushPayload.MaxBytes);
    }

    [Fact]
    public void A_huge_url_drops_the_body_first()
    {
        string json = PushPayload.Build(Guid.NewGuid(), "Title", "Summary", "https://example.com/" + new string('a', 3400));

        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal("", doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("Title", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Gone_subscriptions_are_removed_and_the_rest_are_kept()
    {
        var repo = new FakePushSubscriptions();
        repo.Add("https://fcm.googleapis.com/ok");
        repo.Add("https://fcm.googleapis.com/gone");
        repo.Add("https://fcm.googleapis.com/flaky");
        repo.Add("https://fcm.googleapis.com/throws");
        var transport = new FakeTransport();
        transport.Answers["https://fcm.googleapis.com/gone"] = PushDelivery.Gone;
        transport.Answers["https://fcm.googleapis.com/flaky"] = PushDelivery.Failed;
        transport.Answers["https://fcm.googleapis.com/throws"] = (PushDelivery)99;
        var sender = new PushSender(repo, transport, NullLogger<PushSender>.Instance);

        PushReport report = await sender.SendToAllAsync("{}");

        Assert.Equal(new PushReport(1, 1, 2), report);
        Assert.Equal(["https://fcm.googleapis.com/ok", "https://fcm.googleapis.com/flaky", "https://fcm.googleapis.com/throws"], repo.Rows.Select(r => r.Endpoint));
        Assert.Equal(4, transport.Calls.Count);
    }

    [Fact]
    public async Task A_subscription_whose_key_throws_a_cryptographic_error_is_removed()
    {
        var repo = new FakePushSubscriptions();
        repo.Add("https://fcm.googleapis.com/ok");
        repo.Add("https://fcm.googleapis.com/badkey");
        var transport = new FakeTransport();
        transport.Answers["https://fcm.googleapis.com/badkey"] = (PushDelivery)98;
        var sender = new PushSender(repo, transport, NullLogger<PushSender>.Instance);

        PushReport report = await sender.SendToAllAsync("{}");

        Assert.Equal(new PushReport(1, 1, 0), report);
        Assert.Equal("https://fcm.googleapis.com/ok", Assert.Single(repo.Rows).Endpoint);
    }

    [Fact]
    public async Task Sending_to_nobody_is_a_no_op()
    {
        var sender = new PushSender(new FakePushSubscriptions(), new FakeTransport(), NullLogger<PushSender>.Instance);

        Assert.Equal(new PushReport(0, 0, 0), await sender.SendToAllAsync("{}"));
    }

    [Fact]
    public async Task More_than_one_page_of_subscriptions_is_all_reached()
    {
        var repo = new FakePushSubscriptions();
        for (int i = 0; i < 450; i++)
        {
            repo.Add("https://fcm.googleapis.com/" + i);
        }
        var transport = new FakeTransport();

        PushReport report = await new PushSender(repo, transport, NullLogger<PushSender>.Instance).SendToAllAsync("{}");

        Assert.Equal(450, report.Delivered);
        Assert.Equal(450, transport.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PushDelivery.Gone)]
    [InlineData(HttpStatusCode.Gone, PushDelivery.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests, PushDelivery.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, PushDelivery.Failed)]
    [InlineData(HttpStatusCode.Unauthorized, PushDelivery.Failed)]
    public void Only_404_and_410_mean_gone(HttpStatusCode status, PushDelivery expected)
    {
        Assert.Equal(expected, WebPushTransport.Classify(status));
    }

    [Fact]
    public void The_first_publish_queues_a_push_and_a_republish_does_not()
    {
        var (handler, queue, _) = Handler();
        PostAlertsOptions o = TestOptions.WithPush();
        Guid key = Guid.NewGuid();

        Assert.True(handler.Consider(o, key, "post", "-1,1", "Title", "Summary"));
        Assert.False(handler.Consider(o, key, "post", "-1,1", "Title edited", "Summary"));

        PushJob job = Assert.Single(queue.Jobs);
        Assert.Equal(new PushJob(key, "Title", "Summary"), job);
    }

    [Fact]
    public void Other_document_types_are_ignored()
    {
        var (handler, queue, pushed) = Handler();

        Assert.False(handler.Consider(TestOptions.WithPush(), Guid.NewGuid(), "page", "-1,1", "About", null));
        Assert.Empty(queue.Jobs);
        Assert.Equal(0, pushed.Count());
    }

    [Fact]
    public void Document_type_matching_ignores_case()
    {
        var (handler, queue, _) = Handler();

        Assert.True(handler.Consider(TestOptions.WithPush(), Guid.NewGuid(), "Post", "-1,1", "Title", null));
        Assert.Single(queue.Jobs);
    }

    [Fact]
    public void A_post_under_public_access_never_reaches_the_queue()
    {
        var (handler, queue, pushed) = Handler(new FakeProtection("-1,1,5"));

        Assert.False(handler.Consider(TestOptions.WithPush(), Guid.NewGuid(), "post", "-1,1,5", "Members only", "Secret summary"));
        Assert.Empty(queue.Jobs);
        Assert.Equal(0, pushed.Count());
    }

    [Fact]
    public void Include_protected_lets_a_protected_post_through_the_real_check()
    {
        var access = DispatchProxy.Create<IPublicAccessService, ProtectedAccess>();
        var o = TestOptions.Valid();

        Assert.True(new PublicAccessProtectionCheck(access, TestOptions.Monitor(o)).IsExcluded("-1,1,5"));
        o.IncludeProtected = true;
        Assert.False(new PublicAccessProtectionCheck(access, TestOptions.Monitor(o)).IsExcluded("-1,1,5"));
    }

    [Fact]
    public void An_unprotected_path_is_not_excluded()
    {
        var access = DispatchProxy.Create<IPublicAccessService, ProtectedAccess>();

        Assert.False(new PublicAccessProtectionCheck(access, TestOptions.Monitor(TestOptions.Valid())).IsExcluded("-1,1,7"));
    }

    public class ProtectedAccess : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "IsProtected")
            {
                bool isProtected = (string)args![0]! == "-1,1,5";
                return isProtected ? Attempt.Succeed<PublicAccessEntry?>(null) : Attempt.Fail<PublicAccessEntry?>(null);
            }
            return null;
        }
    }

    [Fact]
    public async Task The_dispatcher_sends_the_payload_with_the_resolved_address()
    {
        var posts = new FakePosts();
        PostCandidate post = Posts.Make("Hello", Posts.Utc(2026, 11, 9));
        posts.Posts.Add(post);
        var pushed = new FakePushed();
        pushed.TryMark(post.Key, DateTime.UtcNow);
        var repo = new FakePushSubscriptions();
        repo.Add("https://fcm.googleapis.com/a");
        var transport = new FakeTransport();
        var service = new PushDispatchService(new PushQueue(), posts, new PushSender(repo, transport, NullLogger<PushSender>.Instance), pushed,
            TestOptions.Monitor(TestOptions.WithPush()), NullLogger<PushDispatchService>.Instance) { Wait = TimeSpan.Zero };

        PushReport? report = await service.DispatchAsync(new PushJob(post.Key, "Hello", "Short"), CancellationToken.None);

        Assert.Equal(new PushReport(1, 0, 0), report);
        Assert.Empty(pushed.QueuedKeys);
        using JsonDocument doc = JsonDocument.Parse(Assert.Single(transport.Calls).Payload);
        Assert.Equal(post.Url, doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("Short", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task The_dispatcher_drops_a_post_it_cannot_find_and_leaves_it_queued()
    {
        var pushed = new FakePushed();
        Guid missing = Guid.NewGuid();
        pushed.TryMark(missing, DateTime.UtcNow);
        var repo = new FakePushSubscriptions();
        repo.Add("https://fcm.googleapis.com/a");
        var transport = new FakeTransport();
        var service = new PushDispatchService(new PushQueue(), new FakePosts(), new PushSender(repo, transport, NullLogger<PushSender>.Instance), pushed,
            TestOptions.Monitor(TestOptions.WithPush()), NullLogger<PushDispatchService>.Instance) { Wait = TimeSpan.Zero, Attempts = 2 };

        Assert.Null(await service.DispatchAsync(new PushJob(missing, "Gone", null), CancellationToken.None));
        Assert.Empty(transport.Calls);
        Assert.Equal([missing], pushed.ListQueued());
    }

    [Fact]
    public void The_seeder_marks_existing_posts_once_so_they_never_push()
    {
        var posts = new FakePosts();
        posts.Posts.Add(Posts.Make("A", Posts.Utc(2026, 1, 1)));
        posts.Posts.Add(Posts.Make("B", Posts.Utc(2026, 1, 2)));
        var pushed = new FakePushed();
        var seeder = new PushSeeder(posts, pushed, new Queue(), TestOptions.Monitor(TestOptions.WithPush()), new FakeTimeProvider(), NullLogger<PushSeeder>.Instance);

        seeder.Handle(new UmbracoApplicationStartedNotification(false));
        posts.Posts.Add(Posts.Make("C", Posts.Utc(2026, 1, 3)));
        seeder.Handle(new UmbracoApplicationStartedNotification(false));

        Assert.Equal(2, pushed.Count());
    }

    [Fact]
    public void A_first_publish_is_marked_queued_and_a_restart_queues_it_again()
    {
        var (handler, queue, pushed) = Handler();
        Guid key = Guid.NewGuid();
        handler.Consider(TestOptions.WithPush(), key, "post", "-1,1", "Title", "Summary");
        Assert.Equal([key], pushed.ListQueued());

        // The site restarts before the dispatcher sends: the queue is empty but the marker says queued.
        var afterRestart = new Queue();
        new PushSeeder(new FakePosts(), pushed, afterRestart, TestOptions.Monitor(TestOptions.WithPush()), new FakeTimeProvider(), NullLogger<PushSeeder>.Instance)
            .Handle(new UmbracoApplicationStartedNotification(false));

        Assert.Equal(new PushJob(key, "", null), Assert.Single(afterRestart.Jobs));
    }

    [Fact]
    public void A_push_that_was_sent_is_not_queued_again_after_a_restart()
    {
        var pushed = new FakePushed();
        Guid key = Guid.NewGuid();
        pushed.TryMark(key, DateTime.UtcNow);
        pushed.MarkSent(key);
        var queue = new Queue();

        new PushSeeder(new FakePosts(), pushed, queue, TestOptions.Monitor(TestOptions.WithPush()), new FakeTimeProvider(), NullLogger<PushSeeder>.Instance)
            .Handle(new UmbracoApplicationStartedNotification(false));

        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public void The_seeder_does_nothing_when_push_is_off()
    {
        var posts = new FakePosts();
        posts.Posts.Add(Posts.Make("A", Posts.Utc(2026, 1, 1)));
        var pushed = new FakePushed();

        new PushSeeder(posts, pushed, new Queue(), TestOptions.Monitor(TestOptions.Valid()), new FakeTimeProvider(), NullLogger<PushSeeder>.Instance)
            .Handle(new UmbracoApplicationStartedNotification(false));

        Assert.Equal(0, pushed.Count());
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc", true)]
    [InlineData("https://web.push.apple.com/abc", true)]
    [InlineData("http://fcm.googleapis.com/x", false)]
    [InlineData("https://evil.com/x", false)]
    [InlineData("https://fcm.googleapis.com.evil.com/x", false)]
    [InlineData("https://notfcm.googleapis.com.example.org/x", false)]
    [InlineData("https://127.0.0.1/x", false)]
    [InlineData("https://localhost/x", false)]
    [InlineData("https://user:pw@fcm.googleapis.com/x", false)]
    [InlineData("https://fcm.googleapis.com:8443/x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not a url", false)]
    public void Only_https_endpoints_on_push_service_hosts_are_allowed(string? endpoint, bool allowed)
    {
        Assert.Equal(allowed, PushEndpointPolicy.IsAllowed(endpoint, Hosts));
    }

    [Fact]
    public void An_endpoint_over_the_length_limit_is_refused()
    {
        Assert.False(PushEndpointPolicy.IsAllowed("https://fcm.googleapis.com/" + new string('a', 1000), Hosts));
    }

    [Fact]
    public void A_key_must_be_a_point_on_the_p256_curve()
    {
        var (pub, _) = VapidKeys.Generate();
        byte[] offCurve = new byte[65];
        offCurve[0] = 0x04;
        System.Security.Cryptography.RandomNumberGenerator.Fill(offCurve.AsSpan(1));
        offCurve[64] ^= 0x01;
        string offCurveKey = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(offCurve);

        Assert.True(PushController.IsP256Point(pub));
        Assert.False(PushController.IsP256Point(offCurveKey));
        Assert.False(PushController.IsP256Point(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[65])));
        Assert.False(PushController.IsP256Point("!!!"));
    }

    [Fact]
    public void Subscription_keys_are_checked_for_length_and_normalised()
    {
        var (pub, _) = VapidKeys.Generate();
        string auth = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(new byte[16]);

        Assert.Equal(pub, PushController.Normalize(pub + "==", 65, 0x04));
        Assert.Equal(auth, PushController.Normalize(auth, 16, null));
        Assert.Null(PushController.Normalize(auth, 65, 0x04));
        Assert.Null(PushController.Normalize("!!!", 16, null));
        Assert.Null(PushController.Normalize(null, 16, null));
        Assert.Null(PushController.Normalize(new string('a', 300), 16, null));
    }
}
