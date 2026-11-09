namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>
/// The server posts to every stored endpoint, so an endpoint must not point at an internal address. Only https
/// endpoints on the listed push service hosts are accepted.
/// </summary>
public static class PushEndpointPolicy
{
    public static bool IsAllowed(string? endpoint, IEnumerable<string>? allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > Data.PushSubscriptionRow.EndpointLength
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            return false;
        }
        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return false;
        }
        return (allowedHosts ?? []).Any(h =>
        {
            string allowed = h.Trim().TrimStart('.').ToLowerInvariant();
            return allowed.Length > 0 && (host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
        });
    }
}
