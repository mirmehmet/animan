using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>Cache-expiry query against real (in-memory) SQLite.</summary>
public class CatalogDbContextIntegrationTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory =
        new(opts => new CatalogDbContext(opts));

    [Fact]
    public async Task StaleQuery_ReturnsOnlyEntriesOlderThanRefreshWindow()
    {
        var now = DateTime.UtcNow;
        await using (var db = _factory.CreateDbContext())
        {
            db.Anime.AddRange(
                new CachedAnime { Id = 1, Title = "Fresh", FetchedAt = now.AddDays(-1) },
                new CachedAnime { Id = 2, Title = "Stale", FetchedAt = now.AddDays(-10) },
                new CachedAnime { Id = 3, Title = "Ancient", FetchedAt = now.AddDays(-30) });
            await db.SaveChangesAsync();
        }

        var cutoff = now.AddDays(-7);
        await using (var db = _factory.CreateDbContext())
        {
            var stale = await db.Anime.AsNoTracking()
                .Where(a => a.FetchedAt < cutoff)
                .OrderBy(a => a.Id)
                .Select(a => a.Title)
                .ToListAsync();

            stale.Should().Equal("Stale", "Ancient");
        }
    }

    [Fact]
    public async Task GenreLink_JoinsAcrossMediaGenres()
    {
        await using (var db = _factory.CreateDbContext())
        {
            db.Anime.Add(new CachedAnime { Id = 10, Title = "Steins;Gate", FetchedAt = DateTime.UtcNow });
            db.Genres.Add(new CachedGenre { Id = 24, MediaType = "anime", Name = "Sci-Fi" });
            db.MediaGenres.Add(new CachedMediaGenre { MediaId = 10, MediaType = "anime", GenreId = 24 });
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var genres = await db.MediaGenres
                .Where(mg => mg.MediaId == 10 && mg.MediaType == "anime")
                .Join(db.Genres, mg => mg.GenreId, g => g.Id, (_, g) => g.Name)
                .ToListAsync();

            genres.Should().Equal("Sci-Fi");
        }
    }

    public void Dispose() => _factory.Dispose();
}
