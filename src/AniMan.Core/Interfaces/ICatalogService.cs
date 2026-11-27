using AniMan.Core.Common;
using AniMan.Core.Domain.Models;

namespace AniMan.Core.Interfaces;

public interface ICatalogService
{
    Task<Result<IReadOnlyList<CachedAnime>>> SearchAnimeAsync(string query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedManga>>> SearchMangaAsync(string query, CancellationToken ct = default);
    Task<Result<CachedAnime>> GetAnimeAsync(int malId, bool force = false, CancellationToken ct = default);

    /// <summary>
    /// Fetches the anime from the source now, like <c>GetAnimeAsync(force: true)</c>, but without
    /// the streaming refresh a normal fetch queues — for snapshot refreshes, which keep their
    /// own platform list (the detail page fetches streaming itself).
    /// </summary>
    Task<Result<CachedAnime>> RefreshAnimeDetailsAsync(int malId, CancellationToken ct = default);
    Task<Result<CachedManga>> GetMangaAsync(int malId, bool force = false, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedEpisode>>> GetAnimeEpisodesAsync(int malId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnime>>> GetCurrentSeasonAsync(CancellationToken ct = default);
    Task<Result<PagedList<CachedAnime>>> GetTopAnimeAsync(int page = 1, CancellationToken ct = default);
    Task<Result<PagedList<CachedManga>>> GetTopMangaAsync(int page = 1, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CachedAnimeStreaming>>> GetAnimeStreamingAsync(int malId, bool force = false, CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> GetGenresAsync(int malId, string mediaType, CancellationToken ct = default);
}
