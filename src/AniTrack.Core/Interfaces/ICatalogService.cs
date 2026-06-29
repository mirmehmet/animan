using AniTrack.Core.Common;
using AniTrack.Core.Domain.Models;

namespace AniTrack.Core.Interfaces;

public interface ICatalogService
{
    Task<Result<IReadOnlyList<CachedAnime>>> SearchAnimeAsync(string query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedManga>>> SearchMangaAsync(string query, CancellationToken ct = default);
    Task<Result<CachedAnime>> GetAnimeAsync(int malId, CancellationToken ct = default);
    Task<Result<CachedManga>> GetMangaAsync(int malId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedEpisode>>> GetAnimeEpisodesAsync(int malId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnime>>> GetCurrentSeasonAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnime>>> GetUpcomingSeasonAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnime>>> GetTopAnimeAsync(int page = 1, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedManga>>> GetTopMangaAsync(int page = 1, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnimeStreaming>>> GetAnimeStreamingAsync(int malId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> GetGenresAsync(int malId, string mediaType, CancellationToken ct = default);
}
