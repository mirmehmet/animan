using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AniMan.Infrastructure.AniList.Dtos;
using AniMan.Infrastructure.MediaSource.Dtos;

namespace AniMan.Infrastructure.AniList;

/// <summary>
/// Translates AniList's GraphQL model into the shared media-source DTOs the rest of the app
/// consumes, so AniList can stand in for the primary source without touching CatalogService.
/// <para>
/// Two differences between the services matter and are handled here rather than
/// downstream: AniList scores on 0–100 where MyAnimeList uses 0–10, and AniList's
/// <c>popularity</c> is a member count rather than MyAnimeList's popularity *rank*.
/// </para>
/// </summary>
internal static partial class AniListMapper
{
    /// <summary>
    /// Entries AniList carries but MyAnimeList does not have no <c>idMal</c>. MAL id is our
    /// primary key, so those are dropped — mapping them would collide every one of them at
    /// id 0. They are a real share of results: 12 of the 50 most recently added anime.
    /// </summary>
    public static IReadOnlyList<AnimeDto> ToAnimeDtos(IEnumerable<AniListMedia>? media) =>
        media is null ? [] : [.. media.Where(HasMalId).Select(ToAnimeDto)];

    public static IReadOnlyList<MangaDto> ToMangaDtos(IEnumerable<AniListMedia>? media) =>
        media is null ? [] : [.. media.Where(HasMalId).Select(ToMangaDto)];

    public static bool HasMalId(AniListMedia media) => media.IdMal is > 0;

    public static AnimeDto ToAnimeDto(AniListMedia media) => new()
    {
        MalId = media.IdMal!.Value,
        Title = PreferredTitle(media.Title),
        TitleJapanese = media.Title?.Native,
        Synopsis = StripHtml(media.Description),
        Type = AnimeFormat(media.Format),
        Status = AnimeStatus(media.Status),
        Episodes = media.Episodes,
        Aired = new DateRangeDto
        {
            From = ToIsoDate(media.StartDate),
            To = ToIsoDate(media.EndDate)
        },
        Season = media.Season?.ToLowerInvariant(),
        Year = media.SeasonYear ?? media.StartDate?.Year,
        Score = ToMalScore(media.AverageScore),
        // Rank and Popularity stay null: AniList exposes no MAL-equivalent rank, and its
        // `popularity` is a member count, so writing it through would corrupt sorting.
        Images = ToImages(media.CoverImage),
        Genres = ToGenres(media.Genres),
        Studios = ToNamedEntities(media.Studios?.Nodes)
    };

    public static MangaDto ToMangaDto(AniListMedia media) => new()
    {
        MalId = media.IdMal!.Value,
        Title = PreferredTitle(media.Title),
        TitleJapanese = media.Title?.Native,
        Synopsis = StripHtml(media.Description),
        Type = MangaFormat(media.Format),
        Status = MangaStatus(media.Status),
        Chapters = media.Chapters,
        Volumes = media.Volumes,
        Published = new DateRangeDto
        {
            From = ToIsoDate(media.StartDate),
            To = ToIsoDate(media.EndDate)
        },
        Score = ToMalScore(media.AverageScore),
        Images = ToImages(media.CoverImage),
        Genres = ToGenres(media.Genres)
        // Serializations: AniList has no equivalent.
    };

    /// <summary>Keeps only the links AniList marks as streaming services.</summary>
    public static IReadOnlyList<StreamingDto> ToStreamingDtos(
        IReadOnlyList<AniListExternalLink>? links) =>
        links is null
            ? []
            : [.. links
                .Where(l => string.Equals(l.Type, "STREAMING", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(l.Site))
                .Select(l => new StreamingDto { Name = l.Site!, Url = l.Url })];

    // ── Field translation ─────────────────────────────────────────────────────

    // Jikan's `title` is the romaji form, so match that and fall back through the
    // other variants rather than showing an empty card.
    private static string PreferredTitle(AniListTitle? title) =>
        title?.Romaji ?? title?.English ?? title?.Native ?? string.Empty;

    private static double? ToMalScore(int? averageScore) =>
        averageScore is null ? null : Math.Round(averageScore.Value / 10.0, 2);

    private static string? AnimeFormat(string? format) => format switch
    {
        "TV" => "TV",
        "TV_SHORT" => "TV Short",
        "MOVIE" => "Movie",
        "SPECIAL" => "Special",
        "OVA" => "OVA",
        "ONA" => "ONA",
        "MUSIC" => "Music",
        _ => format
    };

    private static string? MangaFormat(string? format) => format switch
    {
        "MANGA" => "Manga",
        "NOVEL" => "Light Novel",
        "ONE_SHOT" => "One-shot",
        _ => format
    };

    private static string? AnimeStatus(string? status) => status switch
    {
        "FINISHED" => "Finished Airing",
        "RELEASING" => "Currently Airing",
        "NOT_YET_RELEASED" => "Not yet aired",
        "CANCELLED" => "Cancelled",
        "HIATUS" => "On Hiatus",
        _ => status
    };

    private static string? MangaStatus(string? status) => status switch
    {
        "FINISHED" => "Finished",
        "RELEASING" => "Publishing",
        "NOT_YET_RELEASED" => "Not yet published",
        "CANCELLED" => "Cancelled",
        "HIATUS" => "On Hiatus",
        _ => status
    };

    /// <summary>
    /// AniList dates are fuzzy — any component may be missing. Missing month/day fall back
    /// to January 1st so the result still parses as the ISO string Jikan returns and the
    /// existing views expect.
    /// </summary>
    private static string? ToIsoDate(AniListFuzzyDate? date)
    {
        if (date?.Year is not { } year) return null;

        try
        {
            return new DateTimeOffset(year, date.Month ?? 1, date.Day ?? 1, 0, 0, 0, TimeSpan.Zero)
                .ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ImagesDto? ToImages(AniListCoverImage? cover) =>
        cover is null ? null : new ImagesDto
        {
            Jpg = new ImageVariantsDto
            {
                ImageUrl = cover.Medium ?? cover.Large,
                LargeImageUrl = cover.Large ?? cover.Medium
            }
        };

    /// <summary>
    /// AniList genres are bare strings with no id. They are emitted with
    /// <see cref="GenreDto.MalId"/> 0, which signals CatalogService to resolve the id
    /// by name so a genre stays a single row whichever source supplied it.
    /// </summary>
    private static IReadOnlyList<GenreDto>? ToGenres(IReadOnlyList<string>? genres) =>
        genres is null or { Count: 0 }
            ? null
            : [.. genres.Where(g => !string.IsNullOrWhiteSpace(g))
                        .Select(g => new GenreDto { MalId = 0, Name = g })];

    private static IReadOnlyList<NamedEntityDto>? ToNamedEntities(
        IReadOnlyList<AniListNamedNode>? nodes) =>
        nodes is null or { Count: 0 }
            ? null
            : [.. nodes.Where(n => !string.IsNullOrWhiteSpace(n.Name))
                       .Select(n => new NamedEntityDto { Name = n.Name! })];

    /// <summary>
    /// Descriptions carry markup even when requested as plain text (<c>asHtml: false</c>
    /// still leaves <c>&lt;br&gt;</c>), so tags are removed and entities decoded before
    /// the text is stored.
    /// </summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var withBreaks = LineBreakTag().Replace(html, "\n");
        var text = WebUtility.HtmlDecode(AnyTag().Replace(withBreaks, string.Empty)).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();
}
