using AniMan.Infrastructure;
using FluentAssertions;

namespace AniMan.Tests.Http;

/// <summary>
/// <c>ConnectSafeExternalAsync</c> is the cover client's last line of SSRF defence: it runs
/// on every connection the handler opens (redirect hops included) and checks the address
/// actually being dialled. These tests drive the real handler with literal addresses, which
/// the guard rejects before any socket is opened, so no network access is needed.
/// </summary>
public class CoverConnectionGuardTests
{
    private const string BlockedMarker = "Blocked cover connection";

    private static HttpClient CreateClient() =>
        new(ServiceCollectionExtensions.CreateCoverHandler()) { Timeout = TimeSpan.FromSeconds(3) };

    [Fact]
    public void CoverHandler_BypassesTheSystemProxy()
    {
        // Through a proxy the guard would see the proxy's address instead of the target's:
        // a local proxy (Clash, Fiddler) would block every cover, a public one would hide
        // the real destination from the check.
        using var handler = ServiceCollectionExtensions.CreateCoverHandler();

        handler.UseProxy.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://127.0.0.1/cover.jpg")]
    [InlineData("http://10.0.0.5/cover.jpg")]
    [InlineData("http://172.16.0.1/cover.jpg")]
    [InlineData("http://192.168.1.10/cover.jpg")]
    [InlineData("http://100.64.0.1/cover.jpg")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/cover.jpg")]
    [InlineData("http://[::1]/cover.jpg")]
    [InlineData("http://[::ffff:127.0.0.1]/cover.jpg")]
    [InlineData("http://[fd00::1]/cover.jpg")]
    [InlineData("http://[fe80::1]/cover.jpg")]
    [InlineData("http://localhost/cover.jpg")]
    [InlineData("http://printer.local/cover.jpg")]
    public async Task Connection_ToLocalOrPrivateTarget_IsBlockedByTheGuard(string url)
    {
        using var http = CreateClient();

        var act = () => http.GetAsync(url);

        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.ToString().Should().Contain(BlockedMarker,
            "the guard, not a refused connection, must be what stops the request");
    }

    [Fact]
    public async Task Connection_ToPublicAddress_IsNotBlockedByTheGuard()
    {
        // 203.0.113.0/24 (TEST-NET-3) is public but unassigned: the attempt fails or times
        // out on the network, which is fine — the guard itself must let it through.
        using var http = CreateClient();
        http.Timeout = TimeSpan.FromSeconds(1);

        Exception? failure = null;
        try
        {
            using var response = await http.GetAsync("http://203.0.113.1:9/cover.jpg");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            failure = ex;
        }

        (failure?.ToString() ?? string.Empty).Should().NotContain(BlockedMarker);
    }
}
