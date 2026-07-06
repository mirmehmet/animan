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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
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
