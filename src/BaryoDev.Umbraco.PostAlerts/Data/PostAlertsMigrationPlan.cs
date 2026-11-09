using Umbraco.Cms.Core.Packaging;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public sealed class PostAlertsMigrationPlan() : PackageMigrationPlan("BaryoDev.Umbraco.PostAlerts")
{
    protected override void DefinePlan()
    {
        To<CreateTables>("6e2f9a14-3c58-4d07-b9a1-52d8e7c04f36");
    }
}
