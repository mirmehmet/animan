using AniMan.Core.Common;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Jikan.Dtos;
using AniMan.Infrastructure.MediaSource;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.MediaSource;

/// <summary>
/// The fallback exists because Jikan serves cached requests normally while answering every
/// cache miss with 504. Two properties matter and are pinned here: a working primary is
/// never bypassed (so the app self-heals the moment Jikan recovers), and a failing primary
/// does not take the request down with it.
/// </summary>
public class FallbackMediaClientTests
{
    private readonly Mock<IJikanClient> _primary = new(MockBehavior.Strict);
    private readonly Mock<IJikanClient> _secondary = new(MockBehavior.Strict);

    private FallbackMediaClient CreateClient() => new(
        _primary.Object, _secondary.Object, NullLogger<FallbackMediaClient>.Instance);

    [Fact]
    public async Task SearchAnime_PrimarySucceeds_SecondaryIsNeverCalled()
    {
        _primary.Setup(c => c.SearchAnimeAsync("konosuba", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimePage(30831));

        var result = await CreateClient().SearchAnimeAsync("konosuba");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data!.Single().MalId.Should().Be(30831);
        _secondary.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SearchAnime_PrimaryFails_SecondaryResultIsReturned()
    {
        _primary.Setup(c => c.SearchAnimeAsync("konosuba", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanAnimeDto>>.Failure("504 Gateway Time-out"));
        _secondary.Setup(c => c.SearchAnimeAsync("konosuba", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimePage(30831));

        var result = await CreateClient().SearchAnimeAsync("konosuba");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data!.Single().MalId.Should().Be(30831);
    }

    [Fact]
    public async Task SearchAnime_BothFail_PrimaryErrorIsSurfaced()
    {
        // The UI already words the primary's message for the user; reporting that the
        // backup source is also down is not the more useful thing to show.
        _primary.Setup(c => c.SearchAnimeAsync(It.IsAny<string>(), 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanAnimeDto>>.Failure("jikan is down"));
        _secondary.Setup(c => c.SearchAnimeAsync(It.IsAny<string>(), 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanAnimeDto>>.Failure("anilist is down"));

        var result = await CreateClient().SearchAnimeAsync("konosuba");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("jikan is down");
    }

    [Fact]
    public async Task SearchManga_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.SearchMangaAsync("berserk", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanMangaDto>>.Failure("504"));
        _secondary.Setup(c => c.SearchMangaAsync("berserk", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanMangaDto>>.Success(
                new JikanPagedResult<JikanMangaDto> { Data = [new JikanMangaDto { MalId = 2 }] }));

        var result = await CreateClient().SearchMangaAsync("berserk");

        result.Value!.Data!.Single().MalId.Should().Be(2);
    }

    [Fact]
    public async Task GetAnimeFull_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeFullAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanAnimeDto>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeFullAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanAnimeDto>>.Success(
                new JikanSingleResult<JikanAnimeDto> { Data = new JikanAnimeDto { MalId = 30831 } }));

        var result = await CreateClient().GetAnimeFullAsync(30831);

        result.Value!.Data!.MalId.Should().Be(30831);
    }

    [Fact]
    public async Task GetMangaFull_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetMangaFullAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanMangaDto>>.Failure("504"));
        _secondary.Setup(c => c.GetMangaFullAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanMangaDto>>.Success(
                new JikanSingleResult<JikanMangaDto> { Data = new JikanMangaDto { MalId = 2 } }));

        var result = await CreateClient().GetMangaFullAsync(2);

        result.Value!.Data!.MalId.Should().Be(2);
    }

    [Fact]
    public async Task GetAnimeEpisodes_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeEpisodesAsync(30831, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanEpisodeDto>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeEpisodesAsync(30831, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanEpisodeDto>>.Success(
                new JikanPagedResult<JikanEpisodeDto> { Data = [] }));

        var result = await CreateClient().GetAnimeEpisodesAsync(30831);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetAnimeStreaming_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeStreamingAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>.Success(
                new JikanSingleResult<IReadOnlyList<JikanStreamingDto>>
                {
                    Data = [new JikanStreamingDto { Name = "Crunchyroll" }]
                }));

        var result = await CreateClient().GetAnimeStreamingAsync(30831);

        result.Value!.Data!.Single().Name.Should().Be("Crunchyroll");
    }

    [Fact]
    public async Task GetCurrentSeason_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetCurrentSeasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanAnimeDto>>.Failure("504"));
        _secondary.Setup(c => c.GetCurrentSeasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimePage(1));

        (await CreateClient().GetCurrentSeasonAsync()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetTopAnime_PrimarySucceeds_SecondaryIsNeverCalled()
    {
        // Browse tabs work throughout the outage; they must keep coming from the primary.
        _primary.Setup(c => c.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnimePage(5));

        (await CreateClient().GetTopAnimeAsync()).IsSuccess.Should().BeTrue();
        _secondary.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetTopManga_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetTopMangaAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanMangaDto>>.Failure("504"));
        _secondary.Setup(c => c.GetTopMangaAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanMangaDto>>.Success(
                new JikanPagedResult<JikanMangaDto> { Data = [] }));

        (await CreateClient().GetTopMangaAsync()).IsSuccess.Should().BeTrue();
    }

    private static Result<JikanPagedResult<JikanAnimeDto>> AnimePage(int malId) =>
        Result<JikanPagedResult<JikanAnimeDto>>.Success(
            new JikanPagedResult<JikanAnimeDto> { Data = [new JikanAnimeDto { MalId = malId }] });
}
