using System.Net;
using AniMan.Infrastructure.Tenrai;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Tenrai;

public class TenraiClientTests : IDisposable
{
    private readonly TenraiRateLimiter _limiter = new(NullLogger<TenraiRateLimiter>.Instance);

    private sealed class DelegatingMockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handlerFunc(request, cancellationToken);
    }

    [Fact]
    public async Task SearchAnime_WhenSourceReturnsSuccessJson_DeserializesCorrectly()
    {
        var json = """
            {
                "data": [
                    { "mal_id": 1, "title": "Cowboy Bebop", "episodes": 26, "type": "TV" }
                ],
                "pagination": { "has_next_page": false }
            }
            """;

        var handler = new DelegatingMockHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var client = new TenraiClient(http, _limiter, NullLogger<TenraiClient>.Instance);

        var result = await client.SearchAnimeAsync("cowboy");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().ContainSingle();
        result.Value!.Data![0].MalId.Should().Be(1);
        result.Value!.Data![0].Title.Should().Be("Cowboy Bebop");
    }

    [Fact]
    public async Task SearchAnime_WhenUserCancels_ThrowsOperationCanceledExceptionDirectly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new DelegatingMockHandler((req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var client = new TenraiClient(http, _limiter, NullLogger<TenraiClient>.Instance);

        var act = async () => await client.SearchAnimeAsync("cowboy", ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAnime_WhenNetworkTimesOut_ReturnsFailureWithoutCrashing()
    {
        var handler = new DelegatingMockHandler((req, ct) =>
            throw new TaskCanceledException("HttpClient timeout"));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var client = new TenraiClient(http, _limiter, NullLogger<TenraiClient>.Instance);

        var result = await client.SearchAnimeAsync("cowboy");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task GetAnimeFull_WhenSourceReturns404_ReturnsFailureWithoutCrashing()
    {
        var handler = new DelegatingMockHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var client = new TenraiClient(http, _limiter, NullLogger<TenraiClient>.Instance);

        var result = await client.GetAnimeFullAsync(999999);

        result.IsSuccess.Should().BeFalse();
    }

    // ── Retry pipeline (virtual time: 1 s + 2 s + 4 s of back-off would otherwise be real) ──

    private const string EmptyPageJson = """{ "data": [], "pagination": { "has_next_page": false } }""";

    /// <summary>
    /// A client whose server answers each call with the next status in the list (the last one
    /// repeats); the retry back-off and the rate limiter both run on the same virtual clock.
    /// </summary>
    private sealed class RetryHarness : IDisposable
    {
        private readonly HttpStatusCode[] _statuses;
        private readonly TenraiRateLimiter _limiter;
        private readonly HttpClient _http;

        public RetryHarness(params HttpStatusCode[] statuses)
        {
            _statuses = statuses;
            _limiter = new TenraiRateLimiter(NullLogger<TenraiRateLimiter>.Instance, Time);
            _http = new HttpClient(new DelegatingMockHandler((req, ct) => Task.FromResult(Next())))
            {
                BaseAddress = new Uri("https://api.tenrai.org/v1/")
            };
            Client = new TenraiClient(_http, _limiter, NullLogger<TenraiClient>.Instance, Time);
        }

        public FakeTimeProvider Time { get; } = new();
        public TenraiClient Client { get; }
        public int Calls { get; private set; }

        private HttpResponseMessage Next()
        {
            var status = _statuses[Math.Min(Calls, _statuses.Length - 1)];
            Calls++;
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
                response.Content = new StringContent(EmptyPageJson, System.Text.Encoding.UTF8, "application/json");
            return response;
        }

        public void Dispose()
        {
            _http.Dispose();
            _limiter.Dispose();
        }
    }

    private static async Task<T> RunInVirtualTimeAsync<T>(Task<T> task, FakeTimeProvider time)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10); // let timer continuations run
        }

        task.IsCompleted.Should().BeTrue("the retry pipeline must finish once virtual time has passed");
        return await task;
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientStatus_IsRetried_AndLaterSuccessIsReturned(HttpStatusCode transient)
    {
        using var h = new RetryHarness(transient, transient, HttpStatusCode.OK);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeTrue();
        h.Calls.Should().Be(3, "two transient answers are retried before the success");
    }

    [Fact]
    public async Task PersistentServerError_GivesUpAfterThreeRetries_WithServiceMessage()
    {
        using var h = new RetryHarness(HttpStatusCode.ServiceUnavailable);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not responding");
        h.Calls.Should().Be(4, "one attempt plus three retries");
    }

    [Fact]
    public async Task PersistentRateLimit_GivesUp_WithTooManyRequestsMessage()
    {
        using var h = new RetryHarness(HttpStatusCode.TooManyRequests);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Too many requests");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ClientError_IsFinal_AndNotRetried(HttpStatusCode status)
    {
        using var h = new RetryHarness(status);

        var result = await RunInVirtualTimeAsync(h.Client.GetAnimeFullAsync(1), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not found");
        h.Calls.Should().Be(1, "a 4xx answer is final");
    }

    public void Dispose() => _limiter.Dispose();
}

