using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using AniMan.Infrastructure.Tenrai;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using AniMan.Core.Common;

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
        result.Kind.Should().Be(ErrorKind.Timeout);
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

    // ── Retry pipeline (virtual time: the back-off would otherwise be real seconds) ──

    private const string EmptyPageJson = """{ "data": [], "pagination": { "has_next_page": false } }""";

    /// <summary>
    /// A client whose server answers each call with the next status in the list (the last one
    /// repeats); the retry back-off and the rate limiter both run on the same virtual clock.
    /// </summary>
    private sealed class RetryHarness : IDisposable
    {
        private readonly HttpStatusCode[] _statuses;
        private readonly RetryConditionHeaderValue? _retryAfter;
        private readonly TenraiRateLimiter _limiter;
        private readonly HttpClient _http;

        public RetryHarness(params HttpStatusCode[] statuses) : this(null, statuses) { }

        /// <param name="retryAfter">Sets the <c>Retry-After</c> header on every 429 answer.</param>
        public RetryHarness(RetryConditionHeaderValue? retryAfter, params HttpStatusCode[] statuses)
        {
            _statuses = statuses;
            _retryAfter = retryAfter;
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
            if (status == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = _retryAfter;
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
        using var h = new RetryHarness(transient, HttpStatusCode.OK);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeTrue();
        h.Calls.Should().Be(2, "one transient answer is retried before the success");
    }

    [Fact]
    public async Task PersistentServerError_GivesUpAfterOneRetry_WithServiceMessage()
    {
        using var h = new RetryHarness(HttpStatusCode.ServiceUnavailable);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.ServiceUnavailable);
        h.Calls.Should().Be(2, "one attempt plus one retry, then the AniList fallback takes over");
    }

    [Fact]
    public async Task RateLimit_WithShortRetryAfter_WaitsThatLongBeforeRetrying()
    {
        using var h = new RetryHarness(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)),
            HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var task = h.Client.SearchAnimeAsync("bebop");
        await Task.Delay(50);
        h.Time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        h.Calls.Should().Be(1, "the server asked for 5 s, and the default 1 s back-off must not cut it short");

        var result = await RunInVirtualTimeAsync(task, h.Time);
        result.IsSuccess.Should().BeTrue();
        h.Calls.Should().Be(2);
    }

    [Fact]
    public async Task RateLimit_WithLongRetryAfter_FailsAtOnceForTheFallback()
    {
        using var h = new RetryHarness(
            new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)),
            HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        // No virtual time is advanced: a retry would wait and never finish.
        var task = h.Client.SearchAnimeAsync("bebop");
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(task);

        (await task).Kind.Should().Be(ErrorKind.RateLimited);
        h.Calls.Should().Be(1, "waiting two minutes on Tenrai is worse than asking AniList now");
    }

    [Fact]
    public async Task RetriedCall_UsesASingleRateLimitPermit()
    {
        // Every query fails once, then succeeds: 40 calls make 80 requests. Charging the retry
        // its own permit would need 80 of the 60-per-minute budget and stall past one minute;
        // with one permit per call the 3-per-second window paces them through in ~15 s.
        var seen = new ConcurrentDictionary<string, int>();
        using var http = new HttpClient(new DelegatingMockHandler((req, ct) =>
        {
            var attempt = seen.AddOrUpdate(req.RequestUri!.Query, 1, (_, n) => n + 1);
            return Task.FromResult(attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmptyPageJson, System.Text.Encoding.UTF8, "application/json")
                });
        }))
        { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var time = new FakeTimeProvider();
        using var limiter = new TenraiRateLimiter(NullLogger<TenraiRateLimiter>.Instance, time);
        var client = new TenraiClient(http, limiter, NullLogger<TenraiClient>.Instance, time);

        var all = Task.WhenAll(Enumerable.Range(0, 40).Select(i => client.SearchAnimeAsync($"q{i}")));
        for (var second = 0; second < 30 && !all.IsCompleted; second++)
        {
            await Task.Delay(20); // let continuations run
            time.Advance(TimeSpan.FromSeconds(1));
        }
        await Task.WhenAny(all, Task.Delay(200));

        all.IsCompleted.Should().BeTrue("40 calls fit the 60/minute budget when a retry costs no extra permit");
        (await all).Should().OnlyContain(r => r.IsSuccess);
    }

    [Fact]
    public async Task PersistentRateLimit_GivesUp_WithTooManyRequestsMessage()
    {
        using var h = new RetryHarness(HttpStatusCode.TooManyRequests);

        var result = await RunInVirtualTimeAsync(h.Client.SearchAnimeAsync("bebop"), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.RateLimited);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ClientError_IsFinal_AndNotRetried(HttpStatusCode status)
    {
        using var h = new RetryHarness(status);

        var result = await RunInVirtualTimeAsync(h.Client.GetAnimeFullAsync(1), h.Time);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.NotFound);
        h.Calls.Should().Be(1, "a 4xx answer is final");
    }

    [Fact]
    public async Task DnsFailure_IsNotRetried_SoOfflineFailsFast()
    {
        var calls = 0;
        using var http = new HttpClient(new DelegatingMockHandler((req, ct) =>
        {
            calls++;
            throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");
        }))
        { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var time = new FakeTimeProvider();
        using var limiter = new TenraiRateLimiter(NullLogger<TenraiRateLimiter>.Instance, time);
        var client = new TenraiClient(http, limiter, NullLogger<TenraiClient>.Instance, time);

        // No virtual time is advanced: a retry would wait on the back-off and never finish.
        var task = client.SearchAnimeAsync("bebop");
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(task);

        (await task).Kind.Should().Be(ErrorKind.NoConnection);
        calls.Should().Be(1, "an offline machine gets its answer at once instead of after 7 s of retries");
    }

    [Fact]
    public async Task ConnectionFailure_IsStillRetried()
    {
        var calls = 0;
        using var http = new HttpClient(new DelegatingMockHandler((req, ct) =>
        {
            calls++;
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.");
        }))
        { BaseAddress = new Uri("https://api.tenrai.org/v1/") };
        var time = new FakeTimeProvider();
        using var limiter = new TenraiRateLimiter(NullLogger<TenraiRateLimiter>.Instance, time);
        var client = new TenraiClient(http, limiter, NullLogger<TenraiClient>.Instance, time);

        var result = await RunInVirtualTimeAsync(client.SearchAnimeAsync("bebop"), time);

        result.Kind.Should().Be(ErrorKind.NoConnection);
        calls.Should().Be(2, "a refused connection can be a server blip, so it keeps its one retry");
    }

    public void Dispose() => _limiter.Dispose();
}

