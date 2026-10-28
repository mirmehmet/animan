using System.Net;
using AniMan.Core.Domain.Enums;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class CoverStoreTests : IDisposable
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;
        public byte[] Payload = [1, 2, 3];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Payload)
            });
        }
    }

    private readonly StubHandler _handler = new();
    private readonly StoragePaths _paths;
    private readonly CoverStore _store;

    public CoverStoreTests()
    {
        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);

        var factory = Mock.Of<IHttpClientFactory>(
            f => f.CreateClient("covers") == new HttpClient(_handler));
        _store = new CoverStore(factory, _paths, NullLogger<CoverStore>.Instance);
    }

    [Fact]
    public async Task DownloadAsync_FileSchemeUrl_IsRejectedWithoutRequest()
    {
        var result = await _store.DownloadAsync(1, MediaType.Anime, "file:///C:/Windows/win.ini");

        result.Should().BeNull();
        _handler.Calls.Should().Be(0, "non-http(s) URLs must never be requested");
    }

    [Theory]
    [InlineData("http://localhost:8080/cover.jpg")]
    [InlineData("http://127.0.0.1/cover.jpg")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://192.168.1.10/cover.jpg")]
    [InlineData(@"\\attacker\share\cover.jpg")]
    public async Task DownloadAsync_LocalOrMetadataHost_IsRejectedWithoutRequest(string unsafeUrl)
    {
        var result = await _store.DownloadAsync(1, MediaType.Anime, unsafeUrl);

        result.Should().BeNull();
        _handler.Calls.Should().Be(0, "SSRF/UNC targets must be blocked before issuing an HTTP request");
    }

    [Fact]
    public async Task DownloadAsync_RelativeUrl_IsRejectedWithoutRequest()
    {
        var result = await _store.DownloadAsync(1, MediaType.Anime, "not-a-url");

        result.Should().BeNull();
        _handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DownloadAsync_HttpsUrl_DownloadsToCanonicalPath()
    {
        var result = await _store.DownloadAsync(42, MediaType.Manga, "https://example.com/cover.jpg");

        result.Should().Be(_store.GetLocalPath(42, MediaType.Manga));
        _handler.Calls.Should().Be(1);
        (await File.ReadAllBytesAsync(result!)).Should().Equal(1, 2, 3);
        Directory.GetFiles(_paths.CoversPath, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_EmptyResponseBody_ReturnsNullAndWritesNothing()
    {
        _handler.Payload = [];

        var result = await _store.DownloadAsync(42, MediaType.Anime, "https://example.com/empty.jpg");

        result.Should().BeNull();
        File.Exists(_store.GetLocalPath(42, MediaType.Anime)).Should().BeFalse();
        Directory.GetFiles(_paths.CoversPath, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureAsync_ZeroByteExistingFile_RedownloadsValidCover()
    {
        var canonical = _store.GetLocalPath(77, MediaType.Anime);
        await File.WriteAllBytesAsync(canonical, []);

        var result = await _store.EnsureAsync(77, MediaType.Anime, "https://example.com/cover.jpg");

        result.Should().Be(canonical);
        _handler.Calls.Should().Be(1);
        (await File.ReadAllBytesAsync(canonical)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task DownloadAsync_NullUrl_ReturnsNull()
    {
        var result = await _store.DownloadAsync(1, MediaType.Anime, null);

        result.Should().BeNull();
        _handler.Calls.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.CoversPath))
            Directory.Delete(_paths.CoversPath, recursive: true);
    }
}
