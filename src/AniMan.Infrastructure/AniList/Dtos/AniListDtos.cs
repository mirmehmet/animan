using System.Text.Json.Serialization;

namespace AniMan.Infrastructure.AniList.Dtos;

/// <summary>
/// GraphQL envelope. AniList answers HTTP 200 even for query errors, so <see cref="Errors"/>
/// must be checked rather than relying on the status code alone.
/// </summary>
public record AniListResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<AniListError>? Errors { get; init; }
}

public record AniListError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public record AniListPageData
{
    [JsonPropertyName("Page")]
    public AniListPage? Page { get; init; }
}

public record AniListPage
{
    [JsonPropertyName("media")]
    public IReadOnlyList<AniListMedia>? Media { get; init; }

    [JsonPropertyName("pageInfo")]
    public AniListPageInfo? PageInfo { get; init; }
}

public record AniListPageInfo
{
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; init; }
}

public record AniListMediaData
{
    [JsonPropertyName("Media")]
    public AniListMedia? Media { get; init; }
}

public record AniListMedia
{
    /// <summary>MyAnimeList id. Null for entries AniList carries but MAL does not.</summary>
    [JsonPropertyName("idMal")]
    public int? IdMal { get; init; }

    [JsonPropertyName("title")]
    public AniListTitle? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>TV, MOVIE, OVA, ONA, SPECIAL, MUSIC, MANGA, NOVEL, ONE_SHOT.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    /// <summary>FINISHED, RELEASING, NOT_YET_RELEASED, CANCELLED, HIATUS.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; init; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; init; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; init; }

    [JsonPropertyName("startDate")]
    public AniListFuzzyDate? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public AniListFuzzyDate? EndDate { get; init; }

    /// <summary>WINTER, SPRING, SUMMER, FALL.</summary>
    [JsonPropertyName("season")]
    public string? Season { get; init; }

    [JsonPropertyName("seasonYear")]
    public int? SeasonYear { get; init; }

    /// <summary>0–100, unlike MyAnimeList's 0–10.</summary>
    [JsonPropertyName("averageScore")]
    public int? AverageScore { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<string>? Genres { get; init; }

    [JsonPropertyName("coverImage")]
    public AniListCoverImage? CoverImage { get; init; }

    [JsonPropertyName("studios")]
    public AniListStudioConnection? Studios { get; init; }

    [JsonPropertyName("externalLinks")]
    public IReadOnlyList<AniListExternalLink>? ExternalLinks { get; init; }
}

public record AniListTitle
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; init; }

    [JsonPropertyName("english")]
    public string? English { get; init; }

    [JsonPropertyName("native")]
    public string? Native { get; init; }
}

/// <summary>AniList dates are "fuzzy": any component may be missing.</summary>
public record AniListFuzzyDate
{
    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("month")]
    public int? Month { get; init; }

    [JsonPropertyName("day")]
    public int? Day { get; init; }
}

public record AniListCoverImage
{
    [JsonPropertyName("large")]
    public string? Large { get; init; }

    [JsonPropertyName("medium")]
    public string? Medium { get; init; }
}

public record AniListStudioConnection
{
    [JsonPropertyName("nodes")]
    public IReadOnlyList<AniListNamedNode>? Nodes { get; init; }
}

public record AniListNamedNode
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public record AniListExternalLink
{
    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>INFO, SOCIAL or STREAMING.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
