using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>
/// Two kinds of token. The confirmation token is random and only its hash is stored. The unsubscribe token is
/// the subscriber id protected with ASP.NET Core Data Protection, so a digest can carry a working link without
/// the database holding any secret. Share the Data Protection keys between servers.
/// </summary>
public sealed class TokenService(IDataProtectionProvider protection)
{
    private readonly IDataProtector _unsubscribe = protection.CreateProtector("BaryoDev.Umbraco.PostAlerts.Unsubscribe");

    /// <summary>A new random confirmation token (43 URL-safe characters).</summary>
    public string NewConfirmationToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 of the token, as upper-case hex.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public string UnsubscribeToken(Guid subscriberId) => WebEncoders.Base64UrlEncode(_unsubscribe.Protect(subscriberId.ToByteArray()));

    /// <summary>The subscriber id inside an unsubscribe token, or null when the token is not ours.</summary>
    public Guid? ReadUnsubscribeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 600)
        {
            return null;
        }
        try
        {
            byte[] id = _unsubscribe.Unprotect(WebEncoders.Base64UrlDecode(token));
            return id.Length == 16 ? new Guid(id) : null;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
