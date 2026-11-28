using System.Net;
using AniMan.Core.Domain.Enums;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Services;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class CoverStoreTests : IDisposable
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;
        public byte[] Payload = [.. Jpeg];

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

    /// <summary>The smallest payload the store accepts: a JPEG signature plus a byte.</summary>
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private readonly StubHandler _handler = new();
    private readonly TestStorage _storage = new();
    private readonly StoragePaths _paths;
    private readonly CoverStore _store;

    public CoverStoreTests()
    {
        _paths = _storage.Paths;

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
        (await File.ReadAllBytesAsync(result!)).Should().Equal(Jpeg);
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
    public async Task DownloadAsync_NonImageBody_IsNotCachedAsACover()
    {
        // e.g. a CDN error page served with 200 OK
        _handler.Payload = "<html><body>Rate limited</body></html>"u8.ToArray();

        var result = await _store.DownloadAsync(42, MediaType.Anime, "https://example.com/cover.jpg");

        result.Should().BeNull();
        File.Exists(_store.GetLocalPath(42, MediaType.Anime)).Should().BeFalse(
            "a cached non-image would count as a valid cover and never be fetched again");
        Directory.GetFiles(_paths.CoversPath, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }, true)]                                  // JPEG
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, true)]        // PNG
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]                      // GIF89a
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)] // RIFF....WEBP
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, false)] // RIFF....WAVE
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46 }, false)]                                 // truncated RIFF
    [InlineData(new byte[] { 0xFF, 0xD8 }, false)]                                             // truncated JPEG
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, false)]                           // "<html"
    public void IsSupportedImage_ChecksTheFileSignature(byte[] bytes, bool expected)
    {
        CoverStore.IsSupportedImage(bytes).Should().Be(expected);
    }

    [Fact]
    public async Task EnsureAsync_ZeroByteExistingFile_RedownloadsValidCover()
    {
        var canonical = _store.GetLocalPath(77, MediaType.Anime);
        await File.WriteAllBytesAsync(canonical, []);

        var result = await _store.EnsureAsync(77, MediaType.Anime, "https://example.com/cover.jpg");

        result.Should().Be(canonical);
        _handler.Calls.Should().Be(1);
        (await File.ReadAllBytesAsync(canonical)).Should().Equal(Jpeg);
    }

    [Fact]
    public async Task DeleteLeftoverTempFiles_RemovesOnlyOldHalfWrittenDownloads()
    {
        var old = Path.Combine(_paths.CoversPath, "1_anime.jpg.abc.tmp");
        var fresh = Path.Combine(_paths.CoversPath, "2_anime.jpg.def.tmp");
        var cover = Path.Combine(_paths.CoversPath, "3_anime.jpg");
        await File.WriteAllBytesAsync(old, [1]);
        await File.WriteAllBytesAsync(fresh, [1]);
        await File.WriteAllBytesAsync(cover, [1]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddHours(-2));

        _store.DeleteLeftoverTempFiles();

        File.Exists(old).Should().BeFalse("a crash left it behind");
        File.Exists(fresh).Should().BeTrue("it may still be downloading");
        File.Exists(cover).Should().BeTrue("finished covers are never touched");
    }

    // ── AM-130: size limit, cancellation, failures keeping the old cover, sweep ──

    private static readonly byte[] OldCover = [0xFF, 0xD8, 0xFF, 0xE1, 0x01];

    private string WriteExistingCover(int malId)
    {
        var path = _store.GetLocalPath(malId, MediaType.Anime);
        File.WriteAllBytes(path, OldCover);
        return path;
    }

    [Fact]
    public async Task DownloadAsync_BodyOverTheSizeLimit_IsRejected_AndTheOldCoverStays()
    {
        var path = WriteExistingCover(7);
        var oversized = new byte[CoverStore.MaxCoverBytes + 1];
        Jpeg.CopyTo(oversized, 0); // a valid signature: only the size is wrong
        _handler.Payload = oversized;

        var result = await _store.DownloadAsync(7, MediaType.Anime, "https://example.com/huge.jpg");

        result.Should().BeNull();
        (await File.ReadAllBytesAsync(path)).Should().Equal(OldCover);
        Directory.GetFiles(_paths.CoversPath, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_BodyAtTheSizeLimit_IsAccepted()
    {
        var atLimit = new byte[CoverStore.MaxCoverBytes];
        Jpeg.CopyTo(atLimit, 0);
        _handler.Payload = atLimit;

        (await _store.DownloadAsync(8, MediaType.Anime, "https://example.com/big.jpg")).Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_NetworkFailure_ReturnsNull_AndTheOldCoverStays()
    {
        var path = WriteExistingCover(9);
        var offline = new CoverStore(new TestDoubles.StubHttpHandler().Factory(), _paths, NullLogger<CoverStore>.Instance);

        var result = await offline.DownloadAsync(9, MediaType.Anime, "https://example.com/cover.jpg");

        result.Should().BeNull();
        (await File.ReadAllBytesAsync(path)).Should().Equal(OldCover);
    }

    [Fact]
    public async Task DownloadAsync_Cancelled_Throws_AndLeavesNoHalfWrittenFile()
    {
        var path = WriteExistingCover(10);
        using var cts = new CancellationTokenSource();
        var hanging = new HangingHandler();
        var store = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == new HttpClient(hanging)),
            _paths, NullLogger<CoverStore>.Instance);

        var download = store.DownloadAsync(10, MediaType.Anime, "https://example.com/slow.jpg", cts.Token);
        await hanging.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await download.Awaiting(d => d).Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(path)).Should().Equal(OldCover);
        Directory.GetFiles(_paths.CoversPath, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void SweepUnreferenced_DeletesOnlyCoversNoItemUses()
    {
        var kept = WriteExistingCover(1);
        var orphan = WriteExistingCover(2);
        var other = Path.Combine(_paths.CoversPath, "readme.txt");
        File.WriteAllText(other, "not a cover");

        _store.SweepUnreferenced(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(kept).ToUpperInvariant() });

        File.Exists(kept).Should().BeTrue("paths are compared case-insensitively, like Windows");
        File.Exists(orphan).Should().BeFalse();
        File.Exists(other).Should().BeTrue("only *.jpg covers are swept");
    }

    [Fact]
    public void SweepAndTempCleanup_WithoutACoversFolder_DoNothing()
    {
        Directory.Delete(_paths.CoversPath, recursive: true);

        _store.SweepUnreferenced(new HashSet<string>());
        _store.DeleteLeftoverTempFiles();

        Directory.Exists(_paths.CoversPath).Should().BeFalse();
    }

    [Fact]
    public void Delete_RemovesTheCanonicalCover_AndIgnoresAMissingOne()
    {
        var path = WriteExistingCover(3);

        _store.Delete(3, MediaType.Anime);
        _store.Delete(3, MediaType.Anime);

        File.Exists(path).Should().BeFalse();
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
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
        _storage.Dispose();
    }
}
