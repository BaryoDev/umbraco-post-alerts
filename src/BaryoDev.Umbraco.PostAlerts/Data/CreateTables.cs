using Umbraco.Cms.Infrastructure.Migrations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public sealed class CreateTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists(SubscriberRow.TableName))
        {
            Create.Table<SubscriberRow>().Do();
        }
        if (!TableExists(PushSubscriptionRow.TableName))
        {
            Create.Table<PushSubscriptionRow>().Do();
        }
        if (!TableExists(DigestRunRow.TableName))
        {
            Create.Table<DigestRunRow>().Do();
        }
        if (!TableExists(PushedPostRow.TableName))
        {
            Create.Table<PushedPostRow>().Do();
        }
        return Task.CompletedTask;
    }
}
