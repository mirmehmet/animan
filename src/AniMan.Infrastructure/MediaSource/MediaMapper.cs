using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.MediaSource.Dtos;

namespace AniMan.Infrastructure.MediaSource;

internal static partial class MediaMapper
{
    public static CachedAnime ToAnime(AnimeDto dto) => new()
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
        CoverMediumUrl = HttpUrlGuard.SanitizeExternalWebUrl(dto.Images?.Jpg?.ImageUrl ?? dto.Images?.Jpg?.LargeImageUrl),
        CoverLargeUrl = HttpUrlGuard.SanitizeExternalWebUrl(dto.Images?.Jpg?.LargeImageUrl),
        Studios = dto.Studios is { Count: > 0 } s ? string.Join(", ", s.Select(x => x.Name)) : null,
        TitleEnglish = NullIfBlank(dto.TitleEnglish),
        Themes = ToJsonNames(dto.Themes),
        Demographics = ToJsonNames(dto.Demographics),
        DurationMinutes = dto.DurationMinutes ?? ParseDurationMinutes(dto.Duration),
        AgeRating = NullIfBlank(dto.Rating),
        Source = NullIfBlank(dto.Source),
        DataVersion = MediaDataVersion.Current,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedManga ToManga(MangaDto dto) => new()
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
        CoverMediumUrl = HttpUrlGuard.SanitizeExternalWebUrl(dto.Images?.Jpg?.ImageUrl ?? dto.Images?.Jpg?.LargeImageUrl),
        CoverLargeUrl = HttpUrlGuard.SanitizeExternalWebUrl(dto.Images?.Jpg?.LargeImageUrl),
        Serializations = dto.Serializations is { Count: > 0 } x ? string.Join(", ", x.Select(e => e.Name)) : null,
        TitleEnglish = NullIfBlank(dto.TitleEnglish),
        Themes = ToJsonNames(dto.Themes),
        Demographics = ToJsonNames(dto.Demographics),
        Authors = ToJsonNames(dto.Authors),
        DataVersion = MediaDataVersion.Current,
        FetchedAt = DateTime.UtcNow
    };

    public static CachedEpisode ToEpisode(AnimeEpisodeDto dto, int animeId) => new()
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

    public static CachedAnimeStreaming ToStreaming(StreamingDto dto, int animeId) => new()
    {
        AnimeId = animeId,
        PlatformName = dto.Name,
        Url = HttpUrlGuard.SanitizeWebUrl(dto.Url)
    };

    // Explicit genres join the regular list: AniMan shows metadata as the source has it
    // and leaves the judgement to the user.
    public static IReadOnlyList<CachedGenre> ExtractAnimeGenres(AnimeDto dto) =>
        ToGenres(dto.Genres, dto.ExplicitGenres, "anime");

    public static IReadOnlyList<CachedGenre> ExtractMangaGenres(MangaDto dto) =>
        ToGenres(dto.Genres, dto.ExplicitGenres, "manga");

    private static List<CachedGenre> ToGenres(
        IReadOnlyList<GenreDto>? genres, IReadOnlyList<GenreDto>? explicitGenres, string mediaType) =>
        (genres ?? []).Concat(explicitGenres ?? [])
            .Select(g => new CachedGenre
            {
                Id = g.MalId,
                MediaType = mediaType,
                Name = g.Name
            }).ToList();

    /// <summary>
    /// Converts the source's free-text episode length to whole minutes:
    /// "24 min per ep" → 24, "1 hr 45 min" → 105, "2 hr" → 120, "45 sec per ep" → 1.
    /// Returns null for "Unknown", blank, or anything without a recognisable amount.
    /// </summary>
    public static int? ParseDurationMinutes(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return null;

        var seconds = 0;
        foreach (Match m in DurationPart().Matches(duration))
        {
            var amount = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            seconds += m.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "hr" => amount * 3600,
                "min" => amount * 60,
                _ => amount
            };
        }

        if (seconds <= 0) return null;
        // Anything shorter than a minute still counts as one, so a short is never "0 min".
        return Math.Max(1, (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Names as a JSON array (the same shape snapshots use for genres); null when the source sent none.</summary>
    public static string? ToJsonNames(IReadOnlyList<NamedEntityDto>? entities)
    {
        if (entities is null) return null;
        var names = entities
            .Select(e => e.Name?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return JsonSerializer.Serialize(names);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"(?<n>\d+)\s*(?<unit>hr|min|sec)", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPart();
}
