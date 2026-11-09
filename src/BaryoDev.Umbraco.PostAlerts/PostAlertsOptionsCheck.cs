using System.Net.Mail;
using Microsoft.AspNetCore.WebUtilities;

namespace BaryoDev.Umbraco.PostAlerts;

/// <summary>
/// Checks the options without throwing. A mistake turns the affected feature off and shows up in the log and the
/// dashboard, so a typo in a setting never stops the site from starting.
/// </summary>
public static class PostAlertsOptionsCheck
{
    public sealed record Result(IReadOnlyList<string> Email, IReadOnlyList<string> Push)
    {
        public bool EmailReady => Email.Count == 0;
        public bool PushReady => Push.Count == 0;
    }

    // smtpFrom is the Umbraco SMTP From address, used when FromAddress is empty.
    public static Result Check(PostAlertsOptions o, string? smtpFrom = null) => new(CheckEmail(o, smtpFrom), CheckPush(o));

    public static string? EffectiveFrom(PostAlertsOptions o, string? smtpFrom)
    {
        string from = string.IsNullOrWhiteSpace(o.FromAddress) ? (smtpFrom ?? "") : o.FromAddress;
        return IsPlainAddress(from.Trim()) ? from.Trim() : null;
    }

    public static bool TryZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static bool TryBaseUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }
        uri = parsed;
        return true;
    }

    private static List<string> CheckEmail(PostAlertsOptions o, string? smtpFrom)
    {
        var problems = new List<string>();
        if (!o.EnableDigest)
        {
            problems.Add("EnableDigest is false.");
            return problems;
        }
        if (!TryBaseUrl(o.SiteBaseUrl, out _))
        {
            problems.Add("SiteBaseUrl must be an absolute http or https address.");
        }
        if (EffectiveFrom(o, smtpFrom) is null)
        {
            problems.Add("FromAddress is missing or invalid, and Umbraco:CMS:Global:Smtp:From is not set.");
        }
        if (!TryZone(o.TimeZone, out _))
        {
            problems.Add("TimeZone is not a known time zone id.");
        }
        if (o.SendHourLocal is < 0 or > 23)
        {
            problems.Add("SendHourLocal must be 0 to 23.");
        }
        if (!Enum.IsDefined(o.SendDay))
        {
            problems.Add("SendDay is not a day of the week.");
        }
        if (o.DocumentTypes is null || o.DocumentTypes.Length == 0 || o.DocumentTypes.All(string.IsNullOrWhiteSpace))
        {
            problems.Add("DocumentTypes needs at least one alias.");
        }
        if (o.MaxPostsPerDigest is < 1 or > 50)
        {
            problems.Add("MaxPostsPerDigest must be 1 to 50.");
        }
        if (o.ConfirmationHours < 1)
        {
            problems.Add("ConfirmationHours must be at least 1.");
        }
        return problems;
    }

    private static List<string> CheckPush(PostAlertsOptions o)
    {
        var problems = new List<string>();
        bool any = !string.IsNullOrWhiteSpace(o.VapidPublicKey) || !string.IsNullOrWhiteSpace(o.VapidPrivateKey) || !string.IsNullOrWhiteSpace(o.VapidSubject);
        if (!any)
        {
            problems.Add("VapidPublicKey, VapidPrivateKey and VapidSubject are not set.");
            return problems;
        }
        if (!IsKey(o.VapidPublicKey, 65, 0x04))
        {
            problems.Add("VapidPublicKey must be an unpadded base64url P-256 public key (65 bytes).");
        }
        if (!IsKey(o.VapidPrivateKey, 32, null))
        {
            problems.Add("VapidPrivateKey must be an unpadded base64url P-256 private key (32 bytes).");
        }
        string subject = o.VapidSubject?.Trim() ?? "";
        bool mail = subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && subject.Length > 7;
        bool https = subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && subject.Length > 8;
        if (!mail && !https)
        {
            problems.Add("VapidSubject must start with mailto: or https://.");
        }
        if (o.DocumentTypes is null || o.DocumentTypes.Length == 0 || o.DocumentTypes.All(string.IsNullOrWhiteSpace))
        {
            problems.Add("DocumentTypes needs at least one alias.");
        }
        if (o.PushEndpointHosts is null || o.PushEndpointHosts.Length == 0)
        {
            problems.Add("PushEndpointHosts needs at least one entry.");
        }
        return problems;
    }

    private static bool IsKey(string? value, int length, byte? firstByte)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('=') || value.Contains('+') || value.Contains('/'))
        {
            return false;
        }
        try
        {
            byte[] bytes = WebEncoders.Base64UrlDecode(value.Trim());
            return bytes.Length == length && (firstByte is null || bytes[0] == firstByte);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool IsPlainAddress(string value) =>
        value.Length is > 0 and <= 200
        && MailAddress.TryCreate(value, out MailAddress? address)
        && address.Address == value
        && address.Host.Contains('.');
}
