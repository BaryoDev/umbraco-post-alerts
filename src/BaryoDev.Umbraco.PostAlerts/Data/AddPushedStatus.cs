using Umbraco.Cms.Infrastructure.Migrations;

namespace BaryoDev.Umbraco.PostAlerts.Data;

/// <summary>Adds the status column to the pushed-post marker. Rows already there were handled, so they default to sent.</summary>
public sealed class AddPushedStatus(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(PushedPostRow.TableName, "Status"))
        {
            Create.Column("Status").OnTable(PushedPostRow.TableName).AsInt32().NotNullable().WithDefaultValue((int)PushedStatus.Sent).Do();
        }
        return Task.CompletedTask;
    }
}
