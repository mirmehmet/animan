using System.Net;
using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.MediaSource.Dtos;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AniMan.Infrastructure.Tenrai;

/// <summary>
/// Primary media source: <see href="https://tenrai.org/">Tenrai</see>, a fast and reliable MyAnimeList API.
/// The DTOs in <c>MediaSource.Dtos</c> bind to its v1 schema directly.
/// </summary>
public sealed class TenraiClient : IMediaSourceClient
{
    private readonly HttpClient _http;
    private readonly TenraiRateLimiter _rateLimiter;
    private readonly ILogger<TenraiClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly TimeProvider _time;

    /// <summary>
    /// Longest 429 <c>Retry-After</c> wait worth sitting through. Past it the request fails at
    /// once, so the AniList fallback can answer instead of the user waiting on Tenrai.
    /// </summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <param name="timeProvider">Clock for retry back-off and <c>Retry-After</c> dates; tests pass a fake one. Defaults to the system clock.</param>
    public TenraiClient(
        HttpClient http,
        TenraiRateLimiter rateLimiter,
        ILogger<TenraiClient> logger,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>
            {
                TimeProvider = _time
            }
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                // One retry rides out a server blip; a longer outage is the AniList
                // fallback's job, and every extra attempt only delays it.
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                // A 429 says when to come back; null falls back to the 1 s delay above.
                DelayGenerator = args => ValueTask.FromResult(RetryAfter(args.Outcome.Result)),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            })
            .Build();
    }

    /// <summary>
    /// Retry server-side and rate-limit failures; a 4xx answer is final. A failed DNS lookup is
    /// final too: it almost always means the machine is offline, and retrying only adds seconds
    /// of back-off before the cached data can be shown. A 429 asking for a longer wait than
    /// <see cref="MaxRetryAfter"/> is final as well.
    /// </summary>
    private bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
            return false;
        if (outcome.Exception is HttpRequestException) return true;
        if (outcome.Result is not { } response) return false;

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return RetryAfter(response) is not { } wait || wait <= MaxRetryAfter;

        return (int)response.StatusCode >= 500;
    }

    /// <summary>The wait a 429 response asks for (seconds or an HTTP date), or <c>null</c> if none.</summary>
    private TimeSpan? RetryAfter(HttpResponseMessage? response)
    {
        if (response is not { StatusCode: HttpStatusCode.TooManyRequests, Headers.RetryAfter: { } header })
            return null;

        if (header.Delta is { } delta)
            return delta;
        if (header.Date is { } date)
            return date > _time.GetUtcNow() ? date - _time.GetUtcNow() : TimeSpan.Zero;
        return null;
    }

    // Tenrai natively supports the `limit` query parameter (e.g., `?q=konosuba&limit=5` returns 5 results).
    public Task<Result<PagedResult<AnimeDto>>> SearchAnimeAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        GetAsync<PagedResult<AnimeDto>>(
            $"anime?q={Uri.EscapeDataString(query)}&limit={limit}", ct);

    public Task<Result<PagedResult<MangaDto>>> SearchMangaAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        GetAsync<PagedResult<MangaDto>>(
            $"manga?q={Uri.EscapeDataString(query)}&limit={limit}", ct);

    public Task<Result<SingleResult<AnimeDto>>> GetAnimeFullAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<SingleResult<AnimeDto>>($"anime/{malId}/full", ct);

    public Task<Result<SingleResult<MangaDto>>> GetMangaFullAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<SingleResult<MangaDto>>($"manga/{malId}/full", ct);

    public Task<Result<PagedResult<AnimeEpisodeDto>>> GetAnimeEpisodesAsync(
        int malId, int page = 1, CancellationToken ct = default) =>
        GetAsync<PagedResult<AnimeEpisodeDto>>($"anime/{malId}/episodes?page={page}", ct);

    public Task<Result<SingleResult<IReadOnlyList<StreamingDto>>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<SingleResult<IReadOnlyList<StreamingDto>>>($"anime/{malId}/streaming", ct);

    public Task<Result<PagedResult<AnimeDto>>> GetCurrentSeasonAsync(
        CancellationToken ct = default) =>
        GetAsync<PagedResult<AnimeDto>>("seasons/now?limit=25", ct);

    public Task<Result<PagedResult<AnimeDto>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default) =>
        GetAsync<PagedResult<AnimeDto>>($"top/anime?page={page}", ct);

    public Task<Result<PagedResult<MangaDto>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default) =>
        GetAsync<PagedResult<MangaDto>>($"top/manga?page={page}", ct);

    private async Task<Result<T>> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        try
        {
            // One rate-limit permit per logical call, retry included: a retry is rare and
            // single, and charging it a second permit doubled every call's cost in an outage.
            using var response = await _rateLimiter.ExecuteAsync(
                () => _pipeline.ExecuteAsync(
                    async token =>
                    {
                        _logger.LogDebug("GET {Url}", relativeUrl);
                        return await _http.GetAsync(relativeUrl, token).ConfigureAwait(false);
                    }, ct).AsTask(),
                ct).ConfigureAwait(false);

            // A 404/400 is a real answer (unknown MAL id, bad query) — don't let it
            // fall through to the generic "could not reach the API" handler below.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("Not found on MyAnimeList.", ErrorKind.NotFound);
            }

            // Retries are exhausted at this point — turn the final status into a
            // kind that tells the user whose side the problem is on.
            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("The MyAnimeList data service is not responding right now.", ErrorKind.ServiceUnavailable);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("Too many requests to Tenrai.", ErrorKind.RateLimited);
            }

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var dto = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);

            return dto is null
                ? Result<T>.Failure($"Null response from Tenrai: {relativeUrl}")
                : Result<T>.Success(dto);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Tenrai request timed out: {Url}", relativeUrl);
            return Result<T>.Failure("The Tenrai request timed out.", ErrorKind.Timeout);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            // No HTTP answer at all: offline, DNS failure, refused or reset connection.
            _logger.LogWarning(ex, "Tenrai could not be reached: {Url}", relativeUrl);
            return Result<T>.Failure("Could not reach Tenrai.", ErrorKind.NoConnection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenrai request failed: {Url}", relativeUrl);
            return Result<T>.Failure("Tenrai request failed.");
        }
    }
}
