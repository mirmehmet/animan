using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Jikan.Dtos;

namespace AniMan.Infrastructure.Jikan;

internal static class JikanMapper
{
    public static CachedAnime ToAnime(JikanAnimeDto dto) => new()
    {
        Id = dto.MalId,
        Title = dto.Title,
        TitleJapanese = dto.TitleJapanese,
        Synopsis = dto.Synopsis,
        Type = dto.Type,
        Status = dto.Status,
        TotalEpisodes = dto.Episodes,
        AiringStart = dto.Aired?.From,
        AiringEnd = dto.Aired?.To,
        Season = dto.Season,
        Year = dto.Year,
        Score = dto.Score,
        Rank = dto.Rank,
        Popularity = dto.Popularity,
        CoverMediumUrl = dto.Images?.Jpg?.ImageUrl ?? dto.Images?.Jpg?.LargeImageUrl,
        CoverLargeUrl = dto.Images?.Jpg?.LargeImageUrl,
        Studios = dto.Studios is { Count: > 0 } s ? string.Join(", ", s.Select(x => x.Name)) : null,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedManga ToManga(JikanMangaDto dto) => new()
    {
        Id = dto.MalId,
        Title = dto.Title,
        TitleJapanese = dto.TitleJapanese,
        Synopsis = dto.Synopsis,
        Type = dto.Type,
        Status = dto.Status,
        TotalChapters = dto.Chapters,
        TotalVolumes = dto.Volumes,
        PublishedStart = dto.Published?.From,
        PublishedEnd = dto.Published?.To,
        Score = dto.Score,
        Rank = dto.Rank,
        Popularity = dto.Popularity,
        CoverMediumUrl = dto.Images?.Jpg?.ImageUrl ?? dto.Images?.Jpg?.LargeImageUrl,
        CoverLargeUrl = dto.Images?.Jpg?.LargeImageUrl,
        Serializations = dto.Serializations is { Count: > 0 } x ? string.Join(", ", x.Select(e => e.Name)) : null,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedEpisode ToEpisode(JikanEpisodeDto dto, int animeId) => new()
    {
        AnimeId = animeId,
        EpisodeNumber = dto.MalId,
        Title = dto.Title,
        AiredAt = dto.Aired,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedEpisode ToPlaceholderEpisode(int animeId, int episodeNumber) => new()
    {
        AnimeId = animeId,
        EpisodeNumber = episodeNumber,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedAnimeStreaming ToStreaming(JikanStreamingDto dto, int animeId) => new()
    {
        AnimeId = animeId,
        PlatformName = dto.Name,
        Url = dto.Url
    };

    public static IReadOnlyList<CachedGenre> ExtractAnimeGenres(JikanAnimeDto dto) =>
        dto.Genres?.Select(g => new CachedGenre
        {
            Id = g.MalId,
            MediaType = "anime",
            Name = g.Name
        }).ToList() ?? [];

    public static IReadOnlyList<CachedGenre> ExtractMangaGenres(JikanMangaDto dto) =>
        dto.Genres?.Select(g => new CachedGenre
        {
            Id = g.MalId,
            MediaType = "manga",
            Name = g.Name
        }).ToList() ?? [];

}
