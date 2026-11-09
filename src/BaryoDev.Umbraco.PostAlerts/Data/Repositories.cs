using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public sealed record SubscriberCounts(int Pending, int Confirmed);

public interface ISubscriberRepository
{
    SubscriberRow? Get(Guid id);
    SubscriberRow? GetByEmail(string email);
    SubscriberRow? GetByTokenHash(string tokenHash);
    void Add(SubscriberRow row);
    void Update(SubscriberRow row);
    bool Delete(Guid id);
    /// <summary>Deletes pending rows requested before the cutoff. Returns how many.</summary>
    int DeletePendingBefore(DateTime cutoffUtc);
    IReadOnlyList<SubscriberRow> ListConfirmed(int skip, int take);
    SubscriberCounts Counts();
}

public interface IPushSubscriptionRepository
{
    /// <summary>Inserts, or refreshes the keys when the endpoint is already stored.</summary>
    void Upsert(PushSubscriptionRow row);
    bool DeleteByEndpointHash(string endpointHash);
    int Delete(IReadOnlyCollection<Guid> ids);
    IReadOnlyList<PushSubscriptionRow> List(int skip, int take);
    int Count();
}

public interface IDigestRunRepository
{
    DigestRunRow? Latest();
    /// <summary>Inserts the row unless a run already exists for the same period end. True when this caller got the claim.</summary>
    bool TryClaim(DigestRunRow row);
    void Complete(Guid id, DigestRunStatus status, int postCount, int recipients, int sent, DateTime completedUtc);
}

public interface IPushedPostRepository
{
    /// <summary>True when the post was not marked before and is now.</summary>
    bool TryMark(Guid contentKey, DateTime nowUtc);
    int Count();
}

public sealed class SubscriberRepository(IScopeProvider scopeProvider) : ISubscriberRepository
{
    public SubscriberRow? Get(Guid id)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.FirstOrDefault<SubscriberRow>("SELECT * FROM baryoPostAlertsSubscriber WHERE Id = @0", id);
    }

    public SubscriberRow? GetByEmail(string email)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.FirstOrDefault<SubscriberRow>("SELECT * FROM baryoPostAlertsSubscriber WHERE Email = @0", email);
    }

    public SubscriberRow? GetByTokenHash(string tokenHash)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.FirstOrDefault<SubscriberRow>("SELECT * FROM baryoPostAlertsSubscriber WHERE TokenHash = @0", tokenHash);
    }

    public void Add(SubscriberRow row)
    {
        using var scope = scopeProvider.CreateScope();
        scope.Database.Insert(row);
        scope.Complete();
    }

    public void Update(SubscriberRow row)
    {
        using var scope = scopeProvider.CreateScope();
        scope.Database.Update(row);
        scope.Complete();
    }

    public bool Delete(Guid id)
    {
        using var scope = scopeProvider.CreateScope();
        int n = scope.Database.Execute("DELETE FROM baryoPostAlertsSubscriber WHERE Id = @0", id);
        scope.Complete();
        return n > 0;
    }

    public int DeletePendingBefore(DateTime cutoffUtc)
    {
        using var scope = scopeProvider.CreateScope();
        int n = scope.Database.Execute(
            "DELETE FROM baryoPostAlertsSubscriber WHERE Status = 0 AND CreatedUtc < @0",
            cutoffUtc);
        scope.Complete();
        return n;
    }

    public IReadOnlyList<SubscriberRow> ListConfirmed(int skip, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.SkipTake<SubscriberRow>(skip, take,
            "SELECT * FROM baryoPostAlertsSubscriber WHERE Status = @0 ORDER BY CreatedUtc ASC, Id ASC",
            (int)SubscriberStatus.Confirmed);
    }

    public SubscriberCounts Counts()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        int Count(SubscriberStatus s) =>
            scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM baryoPostAlertsSubscriber WHERE Status = @0", (int)s);
        return new SubscriberCounts(Count(SubscriberStatus.Pending), Count(SubscriberStatus.Confirmed));
    }
}

public sealed class PushSubscriptionRepository(IScopeProvider scopeProvider) : IPushSubscriptionRepository
{
    public void Upsert(PushSubscriptionRow row)
    {
        using var scope = scopeProvider.CreateScope();
        int n = scope.Database.Execute(
            "UPDATE baryoPostAlertsPush SET P256dh = @0, Auth = @1 WHERE EndpointHash = @2",
            row.P256dh, row.Auth, row.EndpointHash);
        if (n == 0)
        {
            scope.Database.Insert(row);
        }
        scope.Complete();
    }

    public bool DeleteByEndpointHash(string endpointHash)
    {
        using var scope = scopeProvider.CreateScope();
        int n = scope.Database.Execute("DELETE FROM baryoPostAlertsPush WHERE EndpointHash = @0", endpointHash);
        scope.Complete();
        return n > 0;
    }

    public int Delete(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return 0;
        }
        using var scope = scopeProvider.CreateScope();
        int total = 0;
        foreach (Guid id in ids)
        {
            total += scope.Database.Execute("DELETE FROM baryoPostAlertsPush WHERE Id = @0", id);
        }
        scope.Complete();
        return total;
    }

    public IReadOnlyList<PushSubscriptionRow> List(int skip, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.SkipTake<PushSubscriptionRow>(skip, take,
            "SELECT * FROM baryoPostAlertsPush ORDER BY CreatedUtc ASC, Id ASC");
    }

    public int Count()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM baryoPostAlertsPush");
    }
}

public sealed class DigestRunRepository(IScopeProvider scopeProvider) : IDigestRunRepository
{
    public DigestRunRow? Latest()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.FirstOrDefault<DigestRunRow>(
            "SELECT * FROM baryoPostAlertsDigestRun ORDER BY PeriodEndUtc DESC");
    }

    public bool TryClaim(DigestRunRow row)
    {
        if (Exists(row.PeriodEndUtc))
        {
            return false;
        }
        try
        {
            using var scope = scopeProvider.CreateScope();
            scope.Database.Insert(row);
            scope.Complete();
            return true;
        }
        catch (Exception) when (Exists(row.PeriodEndUtc))
        {
            // Another server inserted the same period between the check and the insert.
            return false;
        }
    }

    public void Complete(Guid id, DigestRunStatus status, int postCount, int recipients, int sent, DateTime completedUtc)
    {
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute(
            "UPDATE baryoPostAlertsDigestRun SET Status = @0, PostCount = @1, Recipients = @2, Sent = @3, CompletedUtc = @4 WHERE Id = @5",
            (int)status, postCount, recipients, sent, completedUtc, id);
        scope.Complete();
    }

    private bool Exists(DateTime periodEndUtc)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM baryoPostAlertsDigestRun WHERE PeriodEndUtc = @0", periodEndUtc) > 0;
    }
}

public sealed class PushedPostRepository(IScopeProvider scopeProvider) : IPushedPostRepository
{
    public bool TryMark(Guid contentKey, DateTime nowUtc)
    {
        using (var scope = scopeProvider.CreateScope(autoComplete: true))
        {
            if (scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM baryoPostAlertsPushed WHERE ContentKey = @0", contentKey) > 0)
            {
                return false;
            }
        }
        try
        {
            using var scope = scopeProvider.CreateScope();
            scope.Database.Insert(new PushedPostRow { ContentKey = contentKey, MarkedUtc = nowUtc });
            scope.Complete();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public int Count()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<int>("SELECT COUNT(*) FROM baryoPostAlertsPushed");
    }
}
