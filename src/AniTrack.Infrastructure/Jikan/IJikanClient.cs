using AniTrack.Core.Common;
using AniTrack.Infrastructure.Jikan.Dtos;

namespace AniTrack.Infrastructure.Jikan;

public interface IJikanClient
{
    Task<Result<JikanPagedResult<JikanAnimeDto>>> SearchAnimeAsync(string query, int limit = 25, CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanMangaDto>>> SearchMangaAsync(string query, int limit = 25, CancellationToken ct = default);
    Task<Result<JikanSingleResult<JikanAnimeDto>>> GetAnimeFullAsync(int malId, CancellationToken ct = default);
    Task<Result<JikanSingleResult<JikanMangaDto>>> GetMangaFullAsync(int malId, CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanEpisodeDto>>> GetAnimeEpisodesAsync(int malId, int page = 1, CancellationToken ct = default);
    Task<Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>> GetAnimeStreamingAsync(int malId, CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanAnimeDto>>> GetCurrentSeasonAsync(CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanAnimeDto>>> GetUpcomingSeasonAsync(CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanAnimeDto>>> GetTopAnimeAsync(int page = 1, CancellationToken ct = default);
    Task<Result<JikanPagedResult<JikanMangaDto>>> GetTopMangaAsync(int page = 1, CancellationToken ct = default);
}
