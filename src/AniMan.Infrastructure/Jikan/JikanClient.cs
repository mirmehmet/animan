using System.Net;
using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Infrastructure.Jikan.Dtos;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AniMan.Infrastructure.Jikan;

public sealed class JikanClient : IJikanClient
{
    private readonly HttpClient _http;
    private readonly JikanRateLimiter _rateLimiter;
    private readonly ILogger<JikanClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JikanClient(HttpClient http, JikanRateLimiter rateLimiter, ILogger<JikanClient> logger)
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
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    (args.Outcome.Result is not null &&
                     ((int)args.Outcome.Result.StatusCode >= 500 ||
                      args.Outcome.Result.StatusCode == HttpStatusCode.TooManyRequests)))
            })
            .Build();
    }

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> SearchAnimeAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanAnimeDto>>(
            $"anime?q={Uri.EscapeDataString(query)}&limit={limit}&sfw=true", ct);

    public Task<Result<JikanPagedResult<JikanMangaDto>>> SearchMangaAsync(
        string query, int limit = 25, CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanMangaDto>>(
            $"manga?q={Uri.EscapeDataString(query)}&limit={limit}&sfw=true", ct);

    public Task<Result<JikanSingleResult<JikanAnimeDto>>> GetAnimeFullAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<JikanSingleResult<JikanAnimeDto>>($"anime/{malId}/full", ct);

    public Task<Result<JikanSingleResult<JikanMangaDto>>> GetMangaFullAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<JikanSingleResult<JikanMangaDto>>($"manga/{malId}/full", ct);

    public Task<Result<JikanPagedResult<JikanEpisodeDto>>> GetAnimeEpisodesAsync(
        int malId, int page = 1, CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanEpisodeDto>>($"anime/{malId}/episodes?page={page}", ct);

    public Task<Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default) =>
        GetAsync<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>($"anime/{malId}/streaming", ct);

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> GetCurrentSeasonAsync(
        CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanAnimeDto>>("seasons/now?limit=25", ct);

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanAnimeDto>>($"top/anime?page={page}", ct);

    public Task<Result<JikanPagedResult<JikanMangaDto>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanMangaDto>>($"top/manga?page={page}", ct);

    private async Task<Result<T>> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        try
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                _logger.LogDebug("GET {Url}", relativeUrl);

                using var response = await _pipeline.ExecuteAsync(
                    async token => await _http.GetAsync(relativeUrl, token).ConfigureAwait(false), ct).ConfigureAwait(false);

                // A 404/400 is a real answer (unknown MAL id, bad query) — don't let it
                // fall through to the generic "could not reach the API" handler below.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                {
                    _logger.LogWarning("Jikan returned {Status} for {Url}", response.StatusCode, relativeUrl);
                    return Result<T>.Failure("Not found on MyAnimeList.");
                }

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var dto = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);

                return dto is null
                    ? Result<T>.Failure($"Null response from Jikan: {relativeUrl}")
                    : Result<T>.Success(dto);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<T>.Failure("Request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jikan request failed: {Url}", relativeUrl);
            return Result<T>.Failure("Could not reach the API. Check your connection and try again.");
        }
    }
}
