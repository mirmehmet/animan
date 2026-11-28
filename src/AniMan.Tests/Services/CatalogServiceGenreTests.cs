using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AniMan.Tests.TestDoubles;

namespace AniMan.Tests.Services;

/// <summary>
/// AniList supplies genres as bare strings with no MyAnimeList id, so they arrive with
/// <c>MalId = 0</c> and must be given an id by name. Getting this wrong splits a genre into
/// two rows — the same "Action" once from each source — which would double it up in Stats.
/// </summary>
[Collection(CatalogStateCollection.Name)]
public class CatalogServiceGenreTests : IDisposable
{
    private const int GeneratedIdBase = 10000;

    private readonly SqliteContextFactory<CatalogDbContext> _factory;
    private readonly CatalogDbContext _db;
    private readonly Mock<IMediaSourceClient> _sourceMock = new(MockBehavior.Strict);
    private readonly Mock<ISettingsService> _settingsMock = new();
    private readonly BackgroundWork _background = new(NullLogger<BackgroundWork>.Instance);

    /// <summary>Ends fire-and-forget cache refreshes before the test database goes away.</summary>
    private void StopBackground()
    {
        _background.StopAndWait(TimeSpan.FromSeconds(5));
        _background.Dispose();
    }

    public CatalogServiceGenreTests()
    {
        _factory = new SqliteContextFactory<CatalogDbContext>(opts => new CatalogDbContext(opts));
        _db = _factory.CreateDbContext();

        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
    }

    private CatalogService CreateService() => new(
        _factory, _sourceMock.Object, _settingsMock.Object, _background,
        NullLogger<CatalogService>.Instance);

    [Fact]
    public async Task IdlessGenre_MatchingAnExistingName_ReusesThatId()
    {
        _db.Genres.Add(new CachedGenre { Id = 1, MediaType = "anime", Name = "Action" });
        await _db.SaveChangesAsync();

        await SearchReturning(AnimeWithGenres(100, "Action"));

        var genres = await _db.Genres.ToListAsync();
        genres.Should().ContainSingle().Which.Id.Should().Be(1);

        var links = await _db.MediaGenres.Where(mg => mg.MediaId == 100).ToListAsync();
        links.Should().ContainSingle().Which.GenreId.Should().Be(1);
    }

    [Fact]
    public async Task IdlessGenre_WithUnknownName_GetsIdFromTheReservedRange()
    {
        await SearchReturning(AnimeWithGenres(100, "Isekai"));

        var genre = await _db.Genres.SingleAsync();
        genre.Name.Should().Be("Isekai");
        genre.Id.Should().BeGreaterThanOrEqualTo(GeneratedIdBase,
            "generated ids must never collide with MyAnimeList's small genre ids");
    }

    [Fact]
    public async Task IdlessGenre_SharedByTwoItemsInOneBatch_CreatesOneRow()
    {
        await SearchReturning(
            AnimeWithGenres(100, "Isekai"),
            AnimeWithGenres(200, "Isekai"));

        var genres = await _db.Genres.Where(g => g.Name == "Isekai").ToListAsync();
        genres.Should().ContainSingle("a genre name maps to exactly one row");

        int[] expectedMediaIds = [100, 200];
        var links = await _db.MediaGenres.Where(mg => mg.GenreId == genres[0].Id).ToListAsync();
        links.Select(l => l.MediaId).Should().BeEquivalentTo(expectedMediaIds);
    }

    [Fact]
    public async Task IdlessGenres_DistinctNames_GetDistinctIds()
    {
        await SearchReturning(AnimeWithGenres(100, "Isekai", "Iyashikei", "Mecha"));

        var genres = await _db.Genres.ToListAsync();
        genres.Should().HaveCount(3);
        genres.Select(g => g.Id).Should().OnlyHaveUniqueItems();
        genres.Should().AllSatisfy(g => g.Id.Should().BeGreaterThanOrEqualTo(GeneratedIdBase));
    }

    [Fact]
    public async Task IdlessGenre_AfterAnEarlierGeneratedId_DoesNotReuseIt()
    {
        await SearchReturning(AnimeWithGenres(100, "Isekai"));
        var first = await _db.Genres.SingleAsync();

        await SearchReturning(AnimeWithGenres(200, "Mecha"));

        var genres = await _db.Genres.ToListAsync();
        genres.Should().HaveCount(2);
        genres.Select(g => g.Id).Should().OnlyHaveUniqueItems();
        genres.Single(g => g.Name == "Mecha").Id.Should().BeGreaterThan(first.Id);
    }

    [Fact]
    public async Task GenreWithRealMalId_IsStoredUnchanged()
    {
        // Tenrai-sourced genres already carry MyAnimeList's id and must not be renumbered.
        var dto = new AnimeDto
        {
            MalId = 100,
            Title = "Test",
            Genres = [new GenreDto { MalId = 4, Name = "Comedy" }]
        };

        await SearchReturning(dto);

        var genre = await _db.Genres.SingleAsync();
        genre.Id.Should().Be(4);
    }

    // ── AM-132: name matching ignores case and surrounding spaces ────────────

    [Theory]
    [InlineData("action")]
    [InlineData("ACTION")]
    [InlineData("  Action  ")]
    public async Task IdlessGenre_MatchesAnExistingName_IgnoringCaseAndSpaces(string incoming)
    {
        _db.Genres.Add(new CachedGenre { Id = 1, MediaType = "anime", Name = "Action" });
        await _db.SaveChangesAsync();

        await SearchReturning(AnimeWithGenres(100, incoming));

        var genre = await _db.Genres.AsNoTracking().SingleAsync();
        (genre.Id, genre.Name).Should().Be((1, "Action"), "the stored row is reused, not renamed");
        (await _db.MediaGenres.SingleAsync(mg => mg.MediaId == 100)).GenreId.Should().Be(1);
    }

    [Fact]
    public async Task IdlessGenre_MatchesAStoredNameThatHasStraySpaces()
    {
        _db.Genres.Add(new CachedGenre { Id = 8, MediaType = "anime", Name = "Drama " });
        await _db.SaveChangesAsync();

        await SearchReturning(AnimeWithGenres(100, "drama"));

        (await _db.Genres.CountAsync()).Should().Be(1);
        (await _db.MediaGenres.SingleAsync(mg => mg.MediaId == 100)).GenreId.Should().Be(8);
    }

    [Fact]
    public async Task IdlessGenres_DifferingOnlyInCase_InOneBatch_BecomeOneTrimmedRow()
    {
        await SearchReturning(
            AnimeWithGenres(100, "Isekai"),
            AnimeWithGenres(200, " isekai "));

        var genre = await _db.Genres.AsNoTracking().SingleAsync();
        genre.Name.Should().Be("Isekai");
        (await _db.MediaGenres.Select(mg => mg.GenreId).Distinct().ToListAsync()).Should().Equal(genre.Id);
    }

    [Fact]
    public async Task BlankGenreNames_AreIgnored()
    {
        await SearchReturning(AnimeWithGenres(100, "   ", "", "Mecha"));

        (await _db.Genres.Select(g => g.Name).ToListAsync()).Should().Equal("Mecha");
        (await _db.MediaGenres.CountAsync(mg => mg.MediaId == 100)).Should().Be(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SearchReturning(params AnimeDto[] dtos)
    {
        var query = Guid.NewGuid().ToString();
        _sourceMock.Setup(j => j.SearchAnimeAsync(query, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(
                new PagedResult<AnimeDto> { Data = dtos }));

        var result = await CreateService().SearchAnimeAsync(query);
        result.IsSuccess.Should().BeTrue();
    }

    private static AnimeDto AnimeWithGenres(int malId, params string[] genreNames) => new()
    {
        MalId = malId,
        Title = $"Anime {malId}",
        // MalId 0 is how a source without MyAnimeList genre ids reports them.
        Genres = [.. genreNames.Select(n => new GenreDto { MalId = 0, Name = n })]
    };

    public void Dispose()
    {
        StopBackground();
        _db.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
