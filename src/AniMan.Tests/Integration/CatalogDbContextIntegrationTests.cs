using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>
/// <see cref="CatalogDbContext.GenreNamesOf"/>, the genre query the catalog and snapshot
/// services both run, against real (in-memory) SQLite. (AM-110: this class used to test a
/// stale-row query the app never runs.)
/// </summary>
public class CatalogDbContextIntegrationTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory =
        new(opts => new CatalogDbContext(opts));

    [Fact]
    public async Task GenreNamesOf_ReturnsTheTitlesGenres()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Genres.AddRange(
                new CachedGenre { Id = 24, MediaType = "anime", Name = "Sci-Fi" },
                new CachedGenre { Id = 41, MediaType = "anime", Name = "Suspense" },
                new CachedGenre { Id = 1, MediaType = "anime", Name = "Action" });
            db.MediaGenres.AddRange(
                new CachedMediaGenre { MediaId = 10, MediaType = "anime", GenreId = 24 },
                new CachedMediaGenre { MediaId = 10, MediaType = "anime", GenreId = 41 },
                new CachedMediaGenre { MediaId = 11, MediaType = "anime", GenreId = 1 });
            await db.SaveChangesAsync();
        }

        await using var read = _factory.CreateDbContext();
        (await read.GenreNamesOf(10, "anime").ToListAsync()).Should().BeEquivalentTo(["Sci-Fi", "Suspense"]);
        (await read.GenreNamesOf(99, "anime").ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GenreNamesOf_KeepsAnimeAndMangaWithTheSameIdApart()
    {
        // MAL numbers anime and manga separately, so id 1 is both Cowboy Bebop and Monster.
        await using (var db = _factory.CreateDbContext())
        {
            db.Genres.AddRange(
                new CachedGenre { Id = 1, MediaType = "anime", Name = "Action" },
                new CachedGenre { Id = 7, MediaType = "manga", Name = "Mystery" });
            db.MediaGenres.AddRange(
                new CachedMediaGenre { MediaId = 1, MediaType = "anime", GenreId = 1 },
                new CachedMediaGenre { MediaId = 1, MediaType = "manga", GenreId = 7 });
            await db.SaveChangesAsync();
        }

        await using var read = _factory.CreateDbContext();
        (await read.GenreNamesOf(1, "anime").ToListAsync()).Should().Equal("Action");
        (await read.GenreNamesOf(1, "manga").ToListAsync()).Should().Equal("Mystery");
    }

    public void Dispose() => _factory.Dispose();
}
