using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Push;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public sealed class Monitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class TestOptions
{
    public static PostAlertsOptions Valid() => new()
    {
        SiteBaseUrl = "https://example.com",
        FromAddress = "alerts@example.com",
        FromName = "Example",
    };

    public static PostAlertsOptions WithPush()
    {
        var (pub, priv) = VapidKeys.Generate();
        PostAlertsOptions o = Valid();
        o.VapidPublicKey = pub;
        o.VapidPrivateKey = priv;
        o.VapidSubject = "mailto:owner@example.com";
        return o;
    }

    public static IOptionsMonitor<PostAlertsOptions> Monitor(PostAlertsOptions o) => new Monitor<PostAlertsOptions>(o);

    public static IOptionsMonitor<GlobalSettings> Global(string? smtpFrom = null) =>
        new Monitor<GlobalSettings>(new GlobalSettings
        {
            Smtp = smtpFrom is null ? null : new SmtpSettings { From = smtpFrom, Host = "localhost" },
        });
}

public sealed class FakeSubscribers : ISubscriberRepository
{
    public List<SubscriberRow> Rows { get; } = [];

    public SubscriberRow? Get(Guid id) => Rows.FirstOrDefault(r => r.Id == id);

    public SubscriberRow? GetByEmail(string email) => Rows.FirstOrDefault(r => r.Email == email);

    public SubscriberRow? GetByTokenHash(string tokenHash) => Rows.FirstOrDefault(r => r.TokenHash == tokenHash);

    public void Add(SubscriberRow row) => Rows.Add(row);

    public void Update(SubscriberRow row)
    {
    }

    public bool Delete(Guid id) => Rows.RemoveAll(r => r.Id == id) > 0;

    public int DeletePendingBefore(DateTime cutoffUtc) =>
        Rows.RemoveAll(r => r.Status == (int)SubscriberStatus.Pending && r.CreatedUtc < cutoffUtc);

    public IReadOnlyList<SubscriberRow> ListConfirmed(int skip, int take) =>
        Rows.Where(r => r.Status == (int)SubscriberStatus.Confirmed).OrderBy(r => r.CreatedUtc).Skip(skip).Take(take).ToList();

    public SubscriberCounts Counts() => new(
        Rows.Count(r => r.Status == (int)SubscriberStatus.Pending),
        Rows.Count(r => r.Status == (int)SubscriberStatus.Confirmed));

    public SubscriberRow AddConfirmed(string email, DateTime? created = null)
    {
        var row = new SubscriberRow
        {
            Id = Guid.NewGuid(),
            Email = email,
            Status = (int)SubscriberStatus.Confirmed,
            CreatedUtc = created ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ConfirmedUtc = created ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        Rows.Add(row);
        return row;
    }
}

public sealed class FakePushSubscriptions : IPushSubscriptionRepository
{
    public List<PushSubscriptionRow> Rows { get; } = [];

    public void Upsert(PushSubscriptionRow row)
    {
        PushSubscriptionRow? existing = Rows.FirstOrDefault(r => r.EndpointHash == row.EndpointHash);
        if (existing is null)
        {
            Rows.Add(row);
            return;
        }
        existing.P256dh = row.P256dh;
        existing.Auth = row.Auth;
    }

    public bool DeleteByEndpointHash(string endpointHash) => Rows.RemoveAll(r => r.EndpointHash == endpointHash) > 0;

    public int Delete(IReadOnlyCollection<Guid> ids) => Rows.RemoveAll(r => ids.Contains(r.Id));

    public IReadOnlyList<PushSubscriptionRow> List(int skip, int take) => Rows.Skip(skip).Take(take).ToList();

    public int Count() => Rows.Count;

    public PushSubscriptionRow Add(string endpoint)
    {
        var row = new PushSubscriptionRow
        {
            Id = Guid.NewGuid(),
            Endpoint = endpoint,
            EndpointHash = TokenService.Hash(endpoint),
            P256dh = "p",
            Auth = "a",
            CreatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        Rows.Add(row);
        return row;
    }
}

public sealed class FakeRuns : IDigestRunRepository
{
    public List<DigestRunRow> Rows { get; } = [];

    public DigestRunRow? Latest() => Rows.OrderByDescending(r => r.PeriodEndUtc).FirstOrDefault();

    public bool TryClaim(DigestRunRow row)
    {
        if (Rows.Any(r => r.PeriodEndUtc == row.PeriodEndUtc))
        {
            return false;
        }
        Rows.Add(row);
        return true;
    }

    public void Complete(Guid id, DigestRunStatus status, int postCount, int recipients, int sent, DateTime completedUtc)
    {
        DigestRunRow row = Rows.Single(r => r.Id == id);
        row.Status = (int)status;
        row.PostCount = postCount;
        row.Recipients = recipients;
        row.Sent = sent;
        row.CompletedUtc = completedUtc;
    }
}

public sealed class FakePushed : IPushedPostRepository
{
    public HashSet<Guid> Keys { get; } = [];

    public bool TryMark(Guid contentKey, DateTime nowUtc) => Keys.Add(contentKey);

    public int Count() => Keys.Count;
}

public sealed class RecordingMailer : IAlertMailer
{
    public List<(OutgoingMail Mail, string Type)> Sent { get; } = [];

    /// <summary>Addresses that make the send throw.</summary>
    public HashSet<string> Failing { get; } = [];

    public Task SendAsync(OutgoingMail mail, string emailType, CancellationToken cancellationToken = default)
    {
        if (Failing.Contains(mail.To))
        {
            throw new InvalidOperationException("smtp down");
        }
        Sent.Add((mail, emailType));
        return Task.CompletedTask;
    }
}

public sealed class FakePosts : IPostSource
{
    public List<PostCandidate> Posts { get; } = [];

    public IReadOnlyList<PostCandidate> GetAll() => Posts;

    public PostCandidate? Get(Guid key) => Posts.FirstOrDefault(p => p.Key == key);

    public IReadOnlyList<Guid> GetAllKeys() => Posts.Select(p => p.Key).ToList();
}

public sealed class FakeTransport : IPushTransport
{
    public Dictionary<string, PushDelivery> Answers { get; } = [];

    public List<(string Endpoint, string Payload)> Calls { get; } = [];

    public Task<PushDelivery> SendAsync(PushSubscriptionRow subscription, string payload, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add((subscription.Endpoint, payload));
        }
        if (Answers.TryGetValue(subscription.Endpoint, out PushDelivery result))
        {
            if (result == (PushDelivery)99)
            {
                throw new InvalidOperationException("boom");
            }
            return Task.FromResult(result);
        }
        return Task.FromResult(PushDelivery.Delivered);
    }
}

public static class Posts
{
    public static PostCandidate Make(string title, DateTime dateUtc, string? summary = "Short summary.", string? cover = "https://example.com/media/a.jpg?width=600", string? url = null) =>
        new(Guid.NewGuid(), title, summary, url ?? "https://example.com/blog/" + title.ToLowerInvariant().Replace(' ', '-') + "/", cover, dateUtc);

    public static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);
}
