using System.Net;
using System.Text.Json;
using AniTrack.Core.Common;
using AniTrack.Infrastructure.Jikan.Dtos;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AniTrack.Infrastructure.Jikan;

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

    public Task<Result<JikanPagedResult<JikanAnimeDto>>> GetUpcomingSeasonAsync(
        CancellationToken ct = default) =>
        GetAsync<JikanPagedResult<JikanAnimeDto>>("seasons/upcoming?limit=25", ct);

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

                var response = await _pipeline.ExecuteAsync(
                    async token => await _http.GetAsync(relativeUrl, token), ct);

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync(ct);
                var dto = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);

                return dto is null
                    ? Result<T>.Failure($"Null response from Jikan: {relativeUrl}")
                    : Result<T>.Success(dto);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return Result<T>.Failure("Request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jikan request failed: {Url}", relativeUrl);
            return Result<T>.Failure(ex.Message);
        }
    }
}
