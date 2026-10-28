using System.Net;
using System.Text.Json;
using AniMan.Core.Common;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TenraiClient(HttpClient http, TenraiRateLimiter rateLimiter, ILogger<TenraiClient> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            })
            .Build();
    }

    /// <summary>Retry server-side and rate-limit failures; a 4xx answer is final.</summary>
    private static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException) return true;
        if (outcome.Result is not { } response) return false;

        return (int)response.StatusCode >= 500 ||
               response.StatusCode == HttpStatusCode.TooManyRequests;
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
            using var response = await _pipeline.ExecuteAsync(
                async token => await _rateLimiter.ExecuteAsync(
                    async () =>
                    {
                        _logger.LogDebug("GET {Url}", relativeUrl);
                        return await _http.GetAsync(relativeUrl, token).ConfigureAwait(false);
                    }, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            // A 404/400 is a real answer (unknown MAL id, bad query) — don't let it
            // fall through to the generic "could not reach the API" handler below.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("Not found on MyAnimeList.");
            }

            // Retries are exhausted at this point — turn the final status into a
            // message that tells the user whose side the problem is on.
            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("The MyAnimeList data service is not responding right now. Please try again later.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Tenrai returned {Status} for {Url}", response.StatusCode, relativeUrl);
                return Result<T>.Failure("Too many requests — please wait a moment and try again.");
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
            return Result<T>.Failure("The request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenrai request failed: {Url}", relativeUrl);
            return Result<T>.Failure("Could not reach the API. Check your connection and try again.");
        }
    }
}
