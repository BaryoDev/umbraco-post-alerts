using System.Net;
using System.Net.Sockets;

namespace BaryoDev.Umbraco.PostAlerts.Services;

public static class ClientKey
{
    /// <summary>IPv4 as is, IPv6 reduced to its /64 so a client rotating inside a prefix shares one bucket.</summary>
    public static string From(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }
        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes).ToString() + "/64";
    }
}
