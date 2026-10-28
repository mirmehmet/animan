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
    public void IsPrivateOrLoopbackIp_ClassifiesAddressesCorrectly(string ipText, bool expectedPrivate)
    {
        var ip = IPAddress.Parse(ipText);

        HttpUrlGuard.IsPrivateOrLoopbackIp(ip).Should().Be(expectedPrivate);
    }
}

