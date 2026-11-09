namespace BaryoDev.Umbraco.PostAlerts.Services;

public static class AbsoluteUrl
{
    /// <summary>
    /// Makes a link absolute against the site address. Absolute http and https links pass through. A path is
    /// added to the site address, keeping any path prefix the site address has. Returns null for anything else.
    /// </summary>
    public static string? Make(string? baseUrl, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = "https:" + url;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? abs))
        {
            return abs.Scheme is "http" or "https" ? abs.AbsoluteUri : null;
        }
        if (!PostAlertsOptionsCheck.TryBaseUrl(baseUrl, out Uri root) || !url.StartsWith('/'))
        {
            return null;
        }
        string prefix = root.AbsolutePath.TrimEnd('/');
        string origin = root.GetLeftPart(UriPartial.Authority);
        string combined = origin + prefix + url;
        return Uri.TryCreate(combined, UriKind.Absolute, out Uri? result) ? result.AbsoluteUri : null;
    }

    /// <summary>The site address with no trailing slash, or null when it is not valid.</summary>
    public static string? Root(string? baseUrl) =>
        PostAlertsOptionsCheck.TryBaseUrl(baseUrl, out Uri root) ? root.GetLeftPart(UriPartial.Authority) + root.AbsolutePath.TrimEnd('/') : null;
}
