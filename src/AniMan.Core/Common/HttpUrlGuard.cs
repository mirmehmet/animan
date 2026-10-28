using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace AniMan.Core.Common;

/// <summary>
/// Central URL validation and SSRF guard for untrusted external URLs
/// (API responses, imported <c>.AniMan</c> backups, cover downloads, and shell launches).
/// </summary>
public static class HttpUrlGuard
{
    private const int MaxUrlLength = 2048;

    /// <summary>
    /// Accepts only well-formed absolute <c>http://</c> or <c>https://</c> URLs
    /// with a non-empty host and no embedded credentials.
    /// </summary>
    public static bool IsAllowedWebUrl(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength)
            return false;

        var trimmed = url.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        if (string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Returns the trimmed URL if it is a valid <c>http://</c> or <c>https://</c> URL; otherwise <c>null</c>.
    /// </summary>
    public static string? SanitizeWebUrl(string? url) =>
        IsAllowedWebUrl(url, out _) ? url!.Trim() : null;

    /// <summary>
    /// Returns the trimmed URL if it is a valid external <c>http://</c> or <c>https://</c> URL
    /// (not targeting loopback, private LAN, or link-local metadata hosts); otherwise <c>null</c>.
    /// </summary>
    public static string? SanitizeExternalWebUrl(string? url) =>
        IsAllowedWebUrl(url, out var uri) && IsSafeExternalHost(uri) ? url!.Trim() : null;

    /// <summary>
    /// Verifies that <paramref name="uri"/> is an <c>http(s)</c> URI whose host is not
    /// <c>localhost</c>, <c>.local</c>, or a loopback/private/link-local IP literal.
    /// </summary>
    public static bool IsSafeExternalHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (IsLocalHostname(host))
            return false;

        if (IPAddress.TryParse(host, out var ip))
            return !IsPrivateOrLoopbackIp(ip);

        return true;
    }

    public static bool IsLocalHostname(string host)
    {
        var normalized = host.TrimEnd('.');
        return string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> for loopback, unspecified, RFC1918 private, link-local,
    /// CGNAT, unique-local IPv6, multicast, or reserved IP addresses.
    /// </summary>
    public static bool IsPrivateOrLoopbackIp(IPAddress ip)
    {
        var addr = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        if (IPAddress.IsLoopback(addr))
            return true;

        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            return bytes[0] == 0                                          // 0.0.0.0/8 (unspecified)
                   || bytes[0] == 10                                      // 10.0.0.0/8 (RFC1918)
                   || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)   // 100.64.0.0/10 (CGNAT)
                   || bytes[0] == 127                                     // 127.0.0.0/8 (loopback)
                   || (bytes[0] == 169 && bytes[1] == 254)                // 169.254.0.0/16 (link-local / cloud metadata)
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)    // 172.16.0.0/12 (RFC1918)
                   || (bytes[0] == 192 && bytes[1] == 168)                // 192.168.0.0/16 (RFC1918)
                   || bytes[0] >= 224;                                    // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.Equals(IPAddress.IPv6Any)
                || addr.Equals(IPAddress.IPv6None)
                || addr.IsIPv6LinkLocal
                || addr.IsIPv6SiteLocal
                || addr.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = addr.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC; // fc00::/7 Unique Local Addresses
        }

        return true;
    }
}

