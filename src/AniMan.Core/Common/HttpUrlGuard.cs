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
    /// Hosts whose covers an imported backup may download. Every cover AniMan itself stores
    /// comes from one of these CDNs, so anything else in a backup file was put there by hand.
    /// </summary>
    private static readonly string[] TrustedCoverDomains = ["myanimelist.net", "anilist.co"];

    /// <summary>
    /// <c>true</c> when <paramref name="url"/> is a safe external URL on a known cover CDN
    /// (MyAnimeList or AniList, including their subdomains).
    /// </summary>
    public static bool IsTrustedCoverUrl(string? url)
    {
        if (!IsAllowedWebUrl(url, out var uri) || !IsSafeExternalHost(uri))
            return false;

        var host = uri.DnsSafeHost.TrimEnd('.');
        return TrustedCoverDomains.Any(domain =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Decides whether a URL may be opened in the user's browser. Links from upstream data must
    /// point outside the machine and its LAN; links the user stored themselves
    /// (<paramref name="allowLocalNetwork"/>, e.g. a home media server) only need to be http(s).
    /// </summary>
    public static bool IsLaunchableWebUrl(
        string? url, bool allowLocalNetwork, [NotNullWhen(true)] out Uri? uri) =>
        IsAllowedWebUrl(url, out uri) && (allowLocalNetwork || IsSafeExternalHost(uri));

    /// <summary>
    /// Returns <c>true</c> for loopback, unspecified, RFC1918 private, link-local, CGNAT,
    /// IETF-reserved, multicast or reserved IPv4 addresses, and for IPv6 addresses that are
    /// loopback, link/site/unique-local, multicast, discard-only, Teredo, or that embed one of
    /// those IPv4 addresses (IPv4-mapped/-compatible, NAT64, 6to4).
    /// <para>
    /// 198.18.0.0/15 is deliberately not blocked: proxy tools in "fake-IP" mode (Clash and
    /// similar) resolve every public host into it, and blocking it would break all covers there.
    /// </para>
    /// </summary>
    public static bool IsPrivateOrLoopbackIp(IPAddress ip)
    {
        var addr = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        if (IPAddress.IsLoopback(addr))
            return true;

        if (addr.AddressFamily == AddressFamily.InterNetwork)
            return IsPrivateIPv4(addr.GetAddressBytes());

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.Equals(IPAddress.IPv6Any)
                || addr.Equals(IPAddress.IPv6None)
                || addr.IsIPv6LinkLocal
                || addr.IsIPv6SiteLocal
                || addr.IsIPv6Multicast
                || addr.IsIPv6Teredo)                                     // 2001::/32 tunnels to an arbitrary IPv4
            {
                return true;
            }

            var bytes = addr.GetAddressBytes();

            if ((bytes[0] & 0xFE) == 0xFC)                                // fc00::/7 unique local
                return true;

            if (StartsWith(bytes, 0x01, 0x00, 0, 0, 0, 0, 0, 0))          // 100::/64 discard-only
                return true;

            if (StartsWith(bytes, 0x00, 0x64, 0xFF, 0x9B, 0x00, 0x01))    // 64:ff9b:1::/48 local-use NAT64
                return true;

            // Formats that carry an IPv4 address inside: judge them by that address.
            if (StartsWith(bytes, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))    // ::a.b.c.d (IPv4-compatible)
                return IsPrivateIPv4(bytes[12..16]);
            if (StartsWith(bytes, 0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0)) // 64:ff9b::/96 NAT64
                return IsPrivateIPv4(bytes[12..16]);
            if (StartsWith(bytes, 0x20, 0x02))                            // 2002::/16 6to4
                return IsPrivateIPv4(bytes[2..6]);

            return false;
        }

        return true;
    }

    private static bool IsPrivateIPv4(byte[] b) =>
        b[0] == 0                                          // 0.0.0.0/8 (unspecified)
        || b[0] == 10                                      // 10.0.0.0/8 (RFC1918)
        || (b[0] == 100 && b[1] is >= 64 and <= 127)       // 100.64.0.0/10 (CGNAT)
        || b[0] == 127                                     // 127.0.0.0/8 (loopback)
        || (b[0] == 169 && b[1] == 254)                    // 169.254.0.0/16 (link-local / cloud metadata)
        || (b[0] == 172 && b[1] is >= 16 and <= 31)        // 172.16.0.0/12 (RFC1918)
        || (b[0] == 192 && b[1] == 0 && b[2] == 0)         // 192.0.0.0/24 (IETF protocol assignments)
        || (b[0] == 192 && b[1] == 168)                    // 192.168.0.0/16 (RFC1918)
        || b[0] >= 224;                                    // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved

    private static bool StartsWith(byte[] bytes, params byte[] prefix) =>
        bytes.AsSpan().StartsWith(prefix);
}
