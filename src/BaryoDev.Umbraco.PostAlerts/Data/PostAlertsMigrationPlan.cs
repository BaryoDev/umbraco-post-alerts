using Umbraco.Cms.Core.Packaging;

namespace BaryoDev.Umbraco.PostAlerts.Data;

public sealed class PostAlertsMigrationPlan() : PackageMigrationPlan("BaryoDev.Umbraco.PostAlerts")
{
    protected override void DefinePlan()
    {
        To<CreateTables>("6e2f9a14-3c58-4d07-b9a1-52d8e7c04f36");
        To<AddPushedStatus>("b3d1c7e5-8a42-4f19-9e60-1c5a7d2f8b04");
    }
}
