using AniMan.Core.Common;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.MediaSource;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.MediaSource;

/// <summary>
/// A source can fail while the app must not. Two properties matter and are pinned here:
/// a working primary is never bypassed (so the app self-heals the moment it recovers), and
/// a failing primary
/// does not take the request down with it.
/// </summary>
public class FallbackMediaClientTests
{
    private readonly Mock<IMediaSourceClient> _primary = new(MockBehavior.Strict);
    private readonly Mock<IMediaSourceClient> _secondary = new(MockBehavior.Strict);

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
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Failure("504 Gateway Time-out"));
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
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Failure("primary is down", ErrorKind.NoConnection));
        _secondary.Setup(c => c.SearchAnimeAsync(It.IsAny<string>(), 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Failure("secondary is down", ErrorKind.ServiceUnavailable));

        var result = await CreateClient().SearchAnimeAsync("konosuba");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("primary is down");
        result.Kind.Should().Be(ErrorKind.NoConnection, "the reason shown to the user follows the primary");
    }

    [Fact]
    public async Task SearchManga_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.SearchMangaAsync("berserk", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<MangaDto>>.Failure("504"));
        _secondary.Setup(c => c.SearchMangaAsync("berserk", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<MangaDto>>.Success(
                new PagedResult<MangaDto> { Data = [new MangaDto { MalId = 2 }] }));

        var result = await CreateClient().SearchMangaAsync("berserk");

        result.Value!.Data!.Single().MalId.Should().Be(2);
    }

    [Fact]
    public async Task GetAnimeFull_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeFullAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeFullAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(
                new SingleResult<AnimeDto> { Data = new AnimeDto { MalId = 30831 } }));

        var result = await CreateClient().GetAnimeFullAsync(30831);

        result.Value!.Data!.MalId.Should().Be(30831);
    }

    [Fact]
    public async Task GetMangaFull_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetMangaFullAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<MangaDto>>.Failure("504"));
        _secondary.Setup(c => c.GetMangaFullAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<MangaDto>>.Success(
                new SingleResult<MangaDto> { Data = new MangaDto { MalId = 2 } }));

        var result = await CreateClient().GetMangaFullAsync(2);

        result.Value!.Data!.MalId.Should().Be(2);
    }

    [Fact]
    public async Task GetAnimeEpisodes_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeEpisodesAsync(30831, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeEpisodesAsync(30831, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Success(
                new PagedResult<AnimeEpisodeDto> { Data = [] }));

        var result = await CreateClient().GetAnimeEpisodesAsync(30831);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetAnimeStreaming_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeStreamingAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("504"));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(30831, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>>
                {
                    Data = [new StreamingDto { Name = "Crunchyroll" }]
                }));

        var result = await CreateClient().GetAnimeStreamingAsync(30831);

        result.Value!.Data!.Single().Name.Should().Be("Crunchyroll");
    }

    [Fact]
    public async Task GetAnimeStreaming_PrimaryReturnsEmpty_FallsBack()
    {
        _primary.Setup(c => c.GetAnimeStreamingAsync(41467, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(41467, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>>
                {
                    Data = [new StreamingDto { Name = "Disney Plus" }]
                }));

        var result = await CreateClient().GetAnimeStreamingAsync(41467);

        result.Value!.Data!.Single().Name.Should().Be("Disney Plus");
    }

    [Fact]
    public async Task GetAnimeStreaming_PrimaryFailsAndSecondaryHasNone_ReportsTheFailure()
    {
        // An empty answer would be cached as "no platforms" and wipe the stored ones.
        _primary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("offline", ErrorKind.NoConnection));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        var result = await CreateClient().GetAnimeStreamingAsync(5114);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.NoConnection);
    }

    [Fact]
    public async Task GetCurrentSeason_PrimaryFails_FallsBack()
    {
        _primary.Setup(c => c.GetCurrentSeasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Failure("504"));
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
            .ReturnsAsync(Result<PagedResult<MangaDto>>.Failure("504"));
        _secondary.Setup(c => c.GetTopMangaAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<MangaDto>>.Success(
                new PagedResult<MangaDto> { Data = [] }));

        (await CreateClient().GetTopMangaAsync()).IsSuccess.Should().BeTrue();
    }

    // ── AM-073: cancellation and the streaming branches where neither source has platforms ──

    [Fact]
    public async Task Cancelled_PrimaryCall_IsNotRetriedOnTheSecondary()
    {
        // Leaving a page cancels its requests; asking the backup source then would only waste
        // its rate limit and could fill a page that no longer exists.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _primary.Setup(c => c.SearchAnimeAsync("konosuba", 25, cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => CreateClient().SearchAnimeAsync("konosuba", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _secondary.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancelled_PrimaryStreamingCall_IsNotRetriedOnTheSecondary()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _primary.Setup(c => c.GetAnimeStreamingAsync(5114, cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => CreateClient().GetAnimeStreamingAsync(5114, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _secondary.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAnimeStreaming_BothFail_ReportsThePrimaryFailure()
    {
        _primary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("offline", ErrorKind.NoConnection));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("down", ErrorKind.ServiceUnavailable));

        var result = await CreateClient().GetAnimeStreamingAsync(5114);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("offline");
        result.Kind.Should().Be(ErrorKind.NoConnection);
    }

    [Fact]
    public async Task GetAnimeStreaming_PrimaryHasNoneAndSecondaryFails_KeepsThePrimarysEmptyAnswer()
    {
        // The primary is the one source allowed to say "this anime has no platforms".
        _primary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));
        _secondary.Setup(c => c.GetAnimeStreamingAsync(5114, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("down", ErrorKind.ServiceUnavailable));

        var result = await CreateClient().GetAnimeStreamingAsync(5114);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().BeEmpty();
    }

    private static Result<PagedResult<AnimeDto>> AnimePage(int malId) =>
        Result<PagedResult<AnimeDto>>.Success(
            new PagedResult<AnimeDto> { Data = [new AnimeDto { MalId = malId }] });
}
