using System.Net;
using AniMan.Core.Common;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class HttpUrlGuardTests
{
    [Theory]
    [InlineData("https://cdn.myanimelist.net/images/anime/1/1.jpg")]
    [InlineData("http://example.com/stream/watch")]
    [InlineData("  https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx1.png  ")]
    public void IsAllowedWebUrl_ValidHttpOrHttps_ReturnsTrue(string url)
    {
        var ok = HttpUrlGuard.IsAllowedWebUrl(url, out var uri);

        ok.Should().BeTrue();
        uri.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path.jpg")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData(@"\\attacker.example.com\share\cover.jpg")]
    [InlineData("https://user:password@example.com/cover.jpg")]
    [InlineData("ftp://example.com/file.jpg")]
    public void IsAllowedWebUrl_DangerousOrMalformedUrl_ReturnsFalse(string? url)
    {
        var ok = HttpUrlGuard.IsAllowedWebUrl(url, out var uri);

        ok.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:8080/cover.jpg")]
    [InlineData("http://sub.localhost/cover.jpg")]
    [InlineData("http://nas.local/cover.jpg")]
    [InlineData("http://127.0.0.1/cover.jpg")]
    [InlineData("http://0.0.0.0/cover.jpg")]
    [InlineData("http://10.0.0.1/cover.jpg")]
    [InlineData("http://100.64.0.1/cover.jpg")]
    [InlineData("http://172.16.5.4/cover.jpg")]
    [InlineData("http://192.168.1.1/cover.jpg")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/cover.jpg")]
    [InlineData("http://[fe80::1]/cover.jpg")]
    [InlineData("http://[fd00::1]/cover.jpg")]
    public void SanitizeExternalWebUrl_PrivateOrLoopbackTarget_ReturnsNull(string url)
    {
        HttpUrlGuard.SanitizeExternalWebUrl(url).Should().BeNull();
    }

    [Fact]
    public void SanitizeExternalWebUrl_PublicHttpsUrl_ReturnsTrimmedUrl()
    {
        var clean = HttpUrlGuard.SanitizeExternalWebUrl("  https://cdn.myanimelist.net/cover.jpg  ");

        clean.Should().Be("https://cdn.myanimelist.net/cover.jpg");
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("100.63.255.255", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.0.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("fc00::1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    // AM-075: blocks the guard used to miss
    [InlineData("192.0.0.8", true)]               // 192.0.0.0/24 IETF protocol assignments
    [InlineData("::127.0.0.1", true)]             // IPv4-compatible IPv6
    [InlineData("::10.0.0.1", true)]
    [InlineData("64:ff9b::7f00:1", true)]         // NAT64 → 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe", true)]      // NAT64 → 169.254.169.254
    [InlineData("64:ff9b:1::1", true)]            // local-use NAT64
    [InlineData("2002:7f00:1::1", true)]          // 6to4 → 127.0.0.1
    [InlineData("2002:c0a8:101::1", true)]        // 6to4 → 192.168.1.1
    [InlineData("2001:0:4136:e378::1", true)]     // Teredo
    [InlineData("100::1", true)]                  // discard-only
    // ...without catching public addresses in the same formats
    [InlineData("64:ff9b::808:808", false)]       // NAT64 → 8.8.8.8
    [InlineData("2002:808:808::1", false)]        // 6to4 → 8.8.8.8
    [InlineData("::8.8.8.8", false)]
    [InlineData("198.18.0.5", false)]             // fake-IP range of Clash-style proxies: must stay usable
    public void IsPrivateOrLoopbackIp_ClassifiesAddressesCorrectly(string ipText, bool expectedPrivate)
    {
        var ip = IPAddress.Parse(ipText);

        HttpUrlGuard.IsPrivateOrLoopbackIp(ip).Should().Be(expectedPrivate);
    }

    [Theory]
    [InlineData("https://cdn.myanimelist.net/images/anime/1/1.jpg", true)]
    [InlineData("https://myanimelist.net/images/anime/1/1.jpg", true)]
    [InlineData("https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx1.png", true)]
    [InlineData("https://CDN.MyAnimeList.net/images/a.jpg", true)]
    [InlineData("https://example.com/cover.jpg", false)]
    [InlineData("https://myanimelist.net.evil.example/cover.jpg", false)]
    [InlineData("https://evilmyanimelist.net/cover.jpg", false)]
    [InlineData("https://anilist.co@evil.example/cover.jpg", false)]
    [InlineData("file:///C:/covers/cover.jpg", false)]
    [InlineData(null, false)]
    public void IsTrustedCoverUrl_AcceptsOnlyTheKnownCoverCdns(string? url, bool expected)
    {
        HttpUrlGuard.IsTrustedCoverUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.crunchyroll.com/series/x", false, true)]
    [InlineData("http://192.168.1.10:32400/web", false, false)]
    [InlineData("http://localhost:8096/", false, false)]
    [InlineData("http://192.168.1.10:32400/web", true, true)]   // the user's own home server link
    [InlineData("file:///C:/Windows/win.ini", true, false)]     // never, whoever stored it
    [InlineData("javascript:alert(1)", true, false)]
    public void IsLaunchableWebUrl_BlocksLocalTargetsUnlessTheUserStoredThem(
        string url, bool allowLocalNetwork, bool expected)
    {
        HttpUrlGuard.IsLaunchableWebUrl(url, allowLocalNetwork, out _).Should().Be(expected);
    }
}

