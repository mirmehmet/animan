using System.Net;
using Moq;

namespace AniMan.Tests.TestDoubles;

/// <summary>
/// HTTP for tests that never leaves the process (AM-112): each request is recorded and answered
/// by <paramref name="respond"/>. The default answer is a connection failure, like being offline.
/// </summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
{
    private readonly List<Uri> _requests = [];

    /// <summary>The smallest body the cover store accepts as an image: a JPEG signature.</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    public IReadOnlyList<Uri> Requests
    {
        get { lock (_requests) return [.. _requests]; }
    }

    /// <summary>Answers every request with <paramref name="body"/> (a JPEG by default).</summary>
    public static StubHttpHandler Returning(byte[]? body = null) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body ?? Jpeg)
    });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_requests) _requests.Add(request.RequestUri!);
        return respond is null
            ? throw new HttpRequestException("Offline (test stub)")
            : Task.FromResult(respond(request));
    }

    /// <summary>An <see cref="IHttpClientFactory"/> whose every client goes through this handler.</summary>
    public IHttpClientFactory Factory()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(this, disposeHandler: false));
        return factory.Object;
    }
}
