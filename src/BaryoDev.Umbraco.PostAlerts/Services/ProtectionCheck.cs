using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>Says whether a content path sits under Umbraco Public Access.</summary>
public interface IProtectionCheck
{
    /// <summary>True when the item must be left out of alerts.</summary>
    bool IsExcluded(string path);
}

public sealed class PublicAccessProtectionCheck(IPublicAccessService access, IOptionsMonitor<PostAlertsOptions> options) : IProtectionCheck
{
    public bool IsExcluded(string path) =>
        !options.CurrentValue.IncludeProtected && access.IsProtected(path).Success;
}
