using AniTrack.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AniTrack.Infrastructure.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CachedAnime> Anime => Set<CachedAnime>();
    public DbSet<CachedEpisode> Episodes => Set<CachedEpisode>();
    public DbSet<CachedAnimeStreaming> AnimeStreaming => Set<CachedAnimeStreaming>();
    public DbSet<CachedManga> Manga => Set<CachedManga>();
    public DbSet<CachedGenre> Genres => Set<CachedGenre>();
    public DbSet<CachedMediaGenre> MediaGenres => Set<CachedMediaGenre>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CachedAnime>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasMany(a => a.Episodes).WithOne(ep => ep.Anime).HasForeignKey(ep => ep.AnimeId);
            e.HasMany(a => a.StreamingPlatforms).WithOne(s => s.Anime).HasForeignKey(s => s.AnimeId);
        });

        modelBuilder.Entity<CachedEpisode>(e => e.HasKey(ep => ep.Id));

        modelBuilder.Entity<CachedAnimeStreaming>(e => e.HasKey(s => s.Id));

        modelBuilder.Entity<CachedManga>(e => e.HasKey(m => m.Id));

        modelBuilder.Entity<CachedGenre>(e => e.HasKey(g => g.Id));

        modelBuilder.Entity<CachedMediaGenre>(e =>
        {
            e.HasKey(mg => new { mg.MediaId, mg.MediaType, mg.GenreId });
            e.HasOne(mg => mg.Genre).WithMany(g => g.MediaGenres).HasForeignKey(mg => mg.GenreId);
        });
    }
}
