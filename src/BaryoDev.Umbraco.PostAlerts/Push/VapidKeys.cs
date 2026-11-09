using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace BaryoDev.Umbraco.PostAlerts.Push;

public static class VapidKeys
{
    /// <summary>A new P-256 key pair as unpadded base64url strings: the 65 byte public key and the 32 byte private key.</summary>
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters p = key.ExportParameters(includePrivateParameters: true);
        byte[] publicKey = new byte[65];
        publicKey[0] = 0x04;
        p.Q.X!.CopyTo(publicKey, 1 + 32 - p.Q.X.Length);
        p.Q.Y!.CopyTo(publicKey, 33 + 32 - p.Q.Y.Length);
        byte[] privateKey = new byte[32];
        p.D!.CopyTo(privateKey, 32 - p.D.Length);
        return (WebEncoders.Base64UrlEncode(publicKey), WebEncoders.Base64UrlEncode(privateKey));
    }
}
