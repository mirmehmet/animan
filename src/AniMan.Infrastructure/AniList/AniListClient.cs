using System.Net.Http.Json;
using System.Text.Json;
using AniMan.Core.Common;
using AniMan.Infrastructure.AniList.Dtos;
using AniMan.Infrastructure.Tenrai;
using AniMan.Infrastructure.MediaSource.Dtos;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.AniList;

/// <summary>
/// AniList GraphQL implementation of the media-source contract, used as a stand-in when
/// the primary cannot answer. Results are shaped into the shared DTOs by <see cref="AniListMapper"/>,
/// so callers cannot tell which service replied.
/// </summary>
public sealed class AniListClient(
    HttpClient http,
    AniListRateLimiter rateLimiter,
    ILogger<AniListClient> logger) : IMediaSourceClient
{
    private const string MediaFields = """
        idMal
        title { romaji english native }
        description(asHtml: false)
        format
        status
        episodes
        chapters
        volumes
        startDate { year month day }
        endDate { year month day }
        season
        seasonYear
        averageScore
        genres
        coverImage { large medium }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Search ────────────────────────────────────────────────────────────────

    public async Task<Result<PagedResult<AnimeDto>>> SearchAnimeAsync(
        string query, int limit = 25, CancellationToken ct = default)
    {
        var result = await QueryPageAsync(
            $$"""
            query ($search: String, $perPage: Int) {
              Page(page: 1, perPage: $perPage) {
                pageInfo { hasNextPage }
                media(search: $search, type: ANIME, sort: SEARCH_MATCH) {
                  {{MediaFields}}
                  studios(isMain: true) { nodes { name } }
                }
              }
            }
            """,
            new { search = query, perPage = limit }, ct).ConfigureAwait(false);

        return ToAnimePage(result);
    }

    public async Task<Result<PagedResult<MangaDto>>> SearchMangaAsync(
        string query, int limit = 25, CancellationToken ct = default)
    {
        var result = await QueryPageAsync(
            $$"""
            query ($search: String, $perPage: Int) {
              Page(page: 1, perPage: $perPage) {
                pageInfo { hasNextPage }
                media(search: $search, type: MANGA, sort: SEARCH_MATCH) {
                  {{MediaFields}}
                }
              }
            }
            """,
            new { search = query, perPage = limit }, ct).ConfigureAwait(false);

        return ToMangaPage(result);
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    public async Task<Result<SingleResult<AnimeDto>>> GetAnimeFullAsync(
        int malId, CancellationToken ct = default)
    {
        var result = await QueryMediaAsync(
            $$"""
            query ($idMal: Int) {
              Media(idMal: $idMal, type: ANIME) {
                {{MediaFields}}
                studios(isMain: true) { nodes { name } }
              }
            }
            """,
            new { idMal = malId }, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
            return Result<SingleResult<AnimeDto>>.Failure(result.Error!);

        var media = result.Value!.Media;
        if (media is null || !AniListMapper.HasMalId(media))
            return Result<SingleResult<AnimeDto>>.Failure($"Anime {malId} not found on AniList");

        return Result<SingleResult<AnimeDto>>.Success(
            new SingleResult<AnimeDto> { Data = AniListMapper.ToAnimeDto(media) });
    }

    public async Task<Result<SingleResult<MangaDto>>> GetMangaFullAsync(
        int malId, CancellationToken ct = default)
    {
        var result = await QueryMediaAsync(
            $$"""
            query ($idMal: Int) {
              Media(idMal: $idMal, type: MANGA) {
                {{MediaFields}}
              }
            }
            """,
            new { idMal = malId }, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
            return Result<SingleResult<MangaDto>>.Failure(result.Error!);

        var media = result.Value!.Media;
        if (media is null || !AniListMapper.HasMalId(media))
            return Result<SingleResult<MangaDto>>.Failure($"Manga {malId} not found on AniList");

        return Result<SingleResult<MangaDto>>.Success(
            new SingleResult<MangaDto> { Data = AniListMapper.ToMangaDto(media) });
    }

    // ── Episodes ──────────────────────────────────────────────────────────────

    /// <summary>
    /// AniList has no dependable per-episode list — <c>streamingEpisodes</c> mixes in
    /// specials and returned 13 entries for a 10-episode season in testing. An empty page
    /// is returned instead of wrong titles: CatalogService then generates placeholder
    /// episodes 1..N from the known total, which is the correct outcome.
    /// </summary>
    public Task<Result<PagedResult<AnimeEpisodeDto>>> GetAnimeEpisodesAsync(
        int malId, int page = 1, CancellationToken ct = default)
    {
        logger.LogDebug("AniList has no episode list for {MalId} — returning empty so placeholders are used", malId);

        return Task.FromResult(Result<PagedResult<AnimeEpisodeDto>>.Success(
            new PagedResult<AnimeEpisodeDto>
            {
                Data = [],
                Pagination = new PaginationDto { HasNextPage = false }
            }));
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    public async Task<Result<SingleResult<IReadOnlyList<StreamingDto>>>> GetAnimeStreamingAsync(
        int malId, CancellationToken ct = default)
    {
        var result = await QueryMediaAsync(
            """
            query ($idMal: Int) {
              Media(idMal: $idMal, type: ANIME) {
                externalLinks { site url type }
              }
            }
            """,
            new { idMal = malId }, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
            return Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure(result.Error!);

        var links = AniListMapper.ToStreamingDtos(result.Value!.Media?.ExternalLinks);
        return Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
            new SingleResult<IReadOnlyList<StreamingDto>> { Data = links });
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    public async Task<Result<PagedResult<AnimeDto>>> GetCurrentSeasonAsync(
        CancellationToken ct = default)
    {
        var (season, year) = CurrentSeason(DateTime.UtcNow);

        var result = await QueryPageAsync(
            $$"""
            query ($season: MediaSeason, $seasonYear: Int) {
              Page(page: 1, perPage: 25) {
                pageInfo { hasNextPage }
                media(season: $season, seasonYear: $seasonYear, type: ANIME,
                      sort: POPULARITY_DESC) {
                  {{MediaFields}}
                  studios(isMain: true) { nodes { name } }
                }
              }
            }
            """,
            new { season, seasonYear = year }, ct).ConfigureAwait(false);

        return ToAnimePage(result);
    }

    public async Task<Result<PagedResult<AnimeDto>>> GetTopAnimeAsync(
        int page = 1, CancellationToken ct = default)
    {
        var result = await QueryPageAsync(
            $$"""
            query ($page: Int) {
              Page(page: $page, perPage: 25) {
                pageInfo { hasNextPage }
                media(type: ANIME, sort: SCORE_DESC) {
                  {{MediaFields}}
                  studios(isMain: true) { nodes { name } }
                }
              }
            }
            """,
            new { page }, ct).ConfigureAwait(false);

        return ToAnimePage(result);
    }

    public async Task<Result<PagedResult<MangaDto>>> GetTopMangaAsync(
        int page = 1, CancellationToken ct = default)
    {
        var result = await QueryPageAsync(
            $$"""
            query ($page: Int) {
              Page(page: $page, perPage: 25) {
                pageInfo { hasNextPage }
                media(type: MANGA, sort: SCORE_DESC) {
                  {{MediaFields}}
                }
              }
            }
            """,
            new { page }, ct).ConfigureAwait(false);

        return ToMangaPage(result);
    }

    /// <summary>Northern-hemisphere seasons, matching AniList's MediaSeason enum.</summary>
    internal static (string Season, int Year) CurrentSeason(DateTime utcNow) =>
        (utcNow.Month switch
        {
            >= 1 and <= 3 => "WINTER",
            >= 4 and <= 6 => "SPRING",
            >= 7 and <= 9 => "SUMMER",
            _ => "FALL"
        }, utcNow.Year);

    // ── Transport ─────────────────────────────────────────────────────────────

    private static Result<PagedResult<AnimeDto>> ToAnimePage(Result<AniListPageData> result) =>
        result.IsSuccess
            ? Result<PagedResult<AnimeDto>>.Success(new PagedResult<AnimeDto>
            {
                Data = AniListMapper.ToAnimeDtos(result.Value!.Page?.Media),
                Pagination = new PaginationDto
                {
                    HasNextPage = result.Value.Page?.PageInfo?.HasNextPage ?? false
                }
            })
            : Result<PagedResult<AnimeDto>>.Failure(result.Error!);

    private static Result<PagedResult<MangaDto>> ToMangaPage(Result<AniListPageData> result) =>
        result.IsSuccess
            ? Result<PagedResult<MangaDto>>.Success(new PagedResult<MangaDto>
            {
                Data = AniListMapper.ToMangaDtos(result.Value!.Page?.Media),
                Pagination = new PaginationDto
                {
                    HasNextPage = result.Value.Page?.PageInfo?.HasNextPage ?? false
                }
            })
            : Result<PagedResult<MangaDto>>.Failure(result.Error!);

    private Task<Result<AniListPageData>> QueryPageAsync(
        string query, object variables, CancellationToken ct) =>
        PostAsync<AniListPageData>(query, variables, ct);

    private Task<Result<AniListMediaData>> QueryMediaAsync(
        string query, object variables, CancellationToken ct) =>
        PostAsync<AniListMediaData>(query, variables, ct);

    private async Task<Result<T>> PostAsync<T>(string query, object variables, CancellationToken ct)
    {
        try
        {
            return await rateLimiter.ExecuteAsync(async () =>
            {
                using var response = await http
                    .PostAsJsonAsync((Uri?)null, new { query, variables }, ct)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var payload = await response.Content
                    .ReadFromJsonAsync<AniListResponse<T>>(JsonOptions, ct)
                    .ConfigureAwait(false);

                // AniList answers 200 with an `errors` array for query-level problems,
                // so a successful status code is not on its own proof of data.
                if (payload?.Errors is { Count: > 0 } errors)
                {
                    var message = errors[0].Message ?? "unknown GraphQL error";
                    logger.LogWarning("AniList returned an error: {Message}", message);
                    return Result<T>.Failure($"AniList error: {message}");
                }

                return payload is { Data: not null }
                    ? Result<T>.Success(payload.Data)
                    : Result<T>.Failure("AniList returned no data");
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "AniList request failed");
            return Result<T>.Failure("Could not reach AniList. Please try again later.");
        }
    }
}
