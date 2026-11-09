using BaryoDev.Umbraco.PostAlerts.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.PostAlerts.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class RepositoryTests : UmbracoIntegrationTest
{
    private ISubscriberRepository Subscribers => GetRequiredService<ISubscriberRepository>();
    private IPushSubscriptionRepository Push => GetRequiredService<IPushSubscriptionRepository>();
    private IDigestRunRepository Runs => GetRequiredService<IDigestRunRepository>();
    private IPushedPostRepository Pushed => GetRequiredService<IPushedPostRepository>();

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Services.TryAddSingleton<ISubscriberRepository, SubscriberRepository>();
        builder.Services.TryAddSingleton<IPushSubscriptionRepository, PushSubscriptionRepository>();
        builder.Services.TryAddSingleton<IDigestRunRepository, DigestRunRepository>();
        builder.Services.TryAddSingleton<IPushedPostRepository, PushedPostRepository>();
    }

    [SetUp]
    public async Task RunPackageMigration()
    {
        var runner = Services.GetRequiredService<PackageMigrationRunner>();
        await runner.RunPackageMigrationsIfPendingAsync("BaryoDev.Umbraco.PostAlerts");

        foreach (string table in new[] { SubscriberRow.TableName, PushSubscriptionRow.TableName, DigestRunRow.TableName, PushedPostRow.TableName })
        {
            using var scope = ScopeProvider.CreateScope(autoComplete: true);
            Assert.That(scope.Database.SqlContext.SqlSyntax.DoesTableExist(scope.Database, table), Is.True, table);
        }
    }

    private static SubscriberRow Sub(string email, SubscriberStatus status = SubscriberStatus.Confirmed, string? hash = null, int day = 1) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        Status = (int)status,
        TokenHash = hash,
        CreatedUtc = new DateTime(2026, 11, day, 0, 0, 0, DateTimeKind.Utc),
    };

    [Test]
    public void Subscribers_are_found_by_email_and_token_hash_and_counted()
    {
        Subscribers.Add(Sub("a@example.com", SubscriberStatus.Pending, "HASH1"));
        Subscribers.Add(Sub("b@example.com"));

        Assert.That(Subscribers.GetByEmail("a@example.com"), Is.Not.Null);
        Assert.That(Subscribers.GetByTokenHash("HASH1")!.Email, Is.EqualTo("a@example.com"));
        Assert.That(Subscribers.GetByTokenHash("nope"), Is.Null);
        Assert.That(Subscribers.Counts(), Is.EqualTo(new SubscriberCounts(1, 1)));
        Assert.That(Subscribers.ListConfirmed(0, 10).Select(r => r.Email), Is.EqualTo(new[] { "b@example.com" }));
    }

    [Test]
    public void A_duplicate_email_is_rejected_by_the_database()
    {
        Subscribers.Add(Sub("a@example.com"));

        Assert.That(() => Subscribers.Add(Sub("a@example.com")), Throws.Exception);
    }

    [Test]
    public void Confirming_updates_the_row_and_delete_removes_it()
    {
        SubscriberRow row = Sub("a@example.com", SubscriberStatus.Pending, "HASH1");
        Subscribers.Add(row);

        row.Status = (int)SubscriberStatus.Confirmed;
        row.TokenHash = null;
        row.ConfirmedUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        Subscribers.Update(row);

        SubscriberRow stored = Subscribers.Get(row.Id)!;
        Assert.That(stored.Status, Is.EqualTo((int)SubscriberStatus.Confirmed));
        Assert.That(stored.TokenHash, Is.Null);
        Assert.That(stored.ConfirmedUtc, Is.Not.Null);
        Assert.That(Subscribers.Delete(row.Id), Is.True);
        Assert.That(Subscribers.Delete(row.Id), Is.False);
        Assert.That(Subscribers.GetByEmail("a@example.com"), Is.Null);
    }

    [Test]
    public void Only_old_pending_rows_are_purged()
    {
        Subscribers.Add(Sub("old@example.com", SubscriberStatus.Pending, "H1", day: 1));
        Subscribers.Add(Sub("new@example.com", SubscriberStatus.Pending, "H2", day: 8));
        Subscribers.Add(Sub("kept@example.com", SubscriberStatus.Confirmed, null, day: 1));

        int removed = Subscribers.DeletePendingBefore(new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(Subscribers.GetByEmail("old@example.com"), Is.Null);
        Assert.That(Subscribers.GetByEmail("new@example.com"), Is.Not.Null);
        Assert.That(Subscribers.GetByEmail("kept@example.com"), Is.Not.Null);
    }

    [Test]
    public void Confirmed_subscribers_page_in_a_stable_order()
    {
        for (int i = 1; i <= 5; i++)
        {
            Subscribers.Add(Sub($"s{i}@example.com", day: i));
        }

        Assert.That(Subscribers.ListConfirmed(0, 2).Select(r => r.Email), Is.EqualTo(new[] { "s1@example.com", "s2@example.com" }));
        Assert.That(Subscribers.ListConfirmed(4, 2).Select(r => r.Email), Is.EqualTo(new[] { "s5@example.com" }));
    }

    [Test]
    public void A_digest_period_can_be_claimed_once()
    {
        var end = new DateTime(2026, 11, 9, 1, 0, 0, DateTimeKind.Utc);
        DigestRunRow Run() => new()
        {
            Id = Guid.NewGuid(),
            PeriodStartUtc = end.AddDays(-7),
            PeriodEndUtc = end,
            Status = (int)DigestRunStatus.Started,
            StartedUtc = end,
        };

        DigestRunRow first = Run();
        Assert.That(Runs.TryClaim(first), Is.True);
        Assert.That(Runs.TryClaim(Run()), Is.False);

        Runs.Complete(first.Id, DigestRunStatus.Sent, 3, 10, 9, end.AddMinutes(1));
        DigestRunRow latest = Runs.Latest()!;
        Assert.That(latest.Status, Is.EqualTo((int)DigestRunStatus.Sent));
        Assert.That((latest.PostCount, latest.Recipients, latest.Sent), Is.EqualTo((3, 10, 9)));
        Assert.That(latest.CompletedUtc, Is.Not.Null);
    }

    [Test]
    public void Latest_run_is_the_one_with_the_newest_period()
    {
        DateTime end = new(2026, 11, 9, 1, 0, 0, DateTimeKind.Utc);
        Runs.TryClaim(new DigestRunRow { Id = Guid.NewGuid(), PeriodStartUtc = end.AddDays(-14), PeriodEndUtc = end.AddDays(-7), StartedUtc = end });
        Runs.TryClaim(new DigestRunRow { Id = Guid.NewGuid(), PeriodStartUtc = end.AddDays(-7), PeriodEndUtc = end, StartedUtc = end });

        Assert.That(Runs.Latest()!.PeriodEndUtc, Is.EqualTo(end));
    }

    [Test]
    public void Push_subscriptions_upsert_by_endpoint_and_delete_by_hash_or_id()
    {
        PushSubscriptionRow Row(string hash, string keys) => new()
        {
            Id = Guid.NewGuid(), EndpointHash = hash, Endpoint = "https://fcm.googleapis.com/" + hash, P256dh = keys, Auth = keys, CreatedUtc = DateTime.UtcNow,
        };

        Push.Upsert(Row("h1", "k1"));
        Push.Upsert(Row("h1", "k2"));
        Push.Upsert(Row("h2", "k3"));

        Assert.That(Push.Count(), Is.EqualTo(2));
        Assert.That(Push.List(0, 10).Single(r => r.EndpointHash == "h1").P256dh, Is.EqualTo("k2"));
        Assert.That(Push.DeleteByEndpointHash("h1"), Is.True);
        Assert.That(Push.DeleteByEndpointHash("h1"), Is.False);
        Assert.That(Push.Delete(Push.List(0, 10).Select(r => r.Id).ToList()), Is.EqualTo(1));
        Assert.That(Push.Count(), Is.EqualTo(0));
    }

    [Test]
    public void A_post_is_marked_for_push_only_once()
    {
        Guid key = Guid.NewGuid();

        Assert.That(Pushed.TryMark(key, DateTime.UtcNow), Is.True);
        Assert.That(Pushed.TryMark(key, DateTime.UtcNow), Is.False);
        Assert.That(Pushed.Count(), Is.EqualTo(1));
    }
}
