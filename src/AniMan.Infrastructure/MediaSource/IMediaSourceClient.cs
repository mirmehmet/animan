using AniMan.Core.Common;
using AniMan.Infrastructure.MediaSource.Dtos;

namespace AniMan.Infrastructure.Tenrai;

public interface IMediaSourceClient
{
    Task<Result<PagedResult<AnimeDto>>> SearchAnimeAsync(string query, int limit = 25, CancellationToken ct = default);
    Task<Result<PagedResult<MangaDto>>> SearchMangaAsync(string query, int limit = 25, CancellationToken ct = default);
    Task<Result<SingleResult<AnimeDto>>> GetAnimeFullAsync(int malId, CancellationToken ct = default);
    Task<Result<SingleResult<MangaDto>>> GetMangaFullAsync(int malId, CancellationToken ct = default);
    Task<Result<PagedResult<AnimeEpisodeDto>>> GetAnimeEpisodesAsync(int malId, int page = 1, CancellationToken ct = default);
    Task<Result<SingleResult<IReadOnlyList<StreamingDto>>>> GetAnimeStreamingAsync(int malId, CancellationToken ct = default);
    Task<Result<PagedResult<AnimeDto>>> GetCurrentSeasonAsync(CancellationToken ct = default);
    Task<Result<PagedResult<AnimeDto>>> GetTopAnimeAsync(int page = 1, CancellationToken ct = default);
    Task<Result<PagedResult<MangaDto>>> GetTopMangaAsync(int page = 1, CancellationToken ct = default);
}
