using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Jikan.Dtos;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

/// <summary>
/// AniList supplies genres as bare strings with no MyAnimeList id, so they arrive with
/// <c>MalId = 0</c> and must be given an id by name. Getting this wrong splits a genre into
/// two rows — the same "Action" once from each source — which would double it up in Stats.
/// </summary>
public class CatalogServiceGenreTests : IDisposable
{
    private const int GeneratedIdBase = 10000;

    private readonly CatalogDbContext _db;
    private readonly IDbContextFactory<CatalogDbContext> _factory;
    private readonly Mock<IJikanClient> _jikanMock = new(MockBehavior.Strict);
    private readonly Mock<ISettingsService> _settingsMock = new();

    public CatalogServiceGenreTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CatalogDbContext(options);
        _db.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<CatalogDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CatalogDbContext(options));
        _factory = factoryMock.Object;

        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
    }

    private CatalogService CreateService() => new(
        _factory, _jikanMock.Object, _settingsMock.Object,
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
        // Jikan-sourced genres already carry MyAnimeList's id and must not be renumbered.
        var dto = new JikanAnimeDto
        {
            MalId = 100,
            Title = "Test",
            Genres = [new JikanGenreDto { MalId = 4, Name = "Comedy" }]
        };

        await SearchReturning(dto);

        var genre = await _db.Genres.SingleAsync();
        genre.Id.Should().Be(4);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SearchReturning(params JikanAnimeDto[] dtos)
    {
        var query = Guid.NewGuid().ToString();
        _jikanMock.Setup(j => j.SearchAnimeAsync(query, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanAnimeDto>>.Success(
                new JikanPagedResult<JikanAnimeDto> { Data = dtos }));

        var result = await CreateService().SearchAnimeAsync(query);
        result.IsSuccess.Should().BeTrue();
    }

    private static JikanAnimeDto AnimeWithGenres(int malId, params string[] genreNames) => new()
    {
        MalId = malId,
        Title = $"Anime {malId}",
        // MalId 0 is how a source without MyAnimeList genre ids reports them.
        Genres = [.. genreNames.Select(n => new JikanGenreDto { MalId = 0, Name = n })]
    };

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
