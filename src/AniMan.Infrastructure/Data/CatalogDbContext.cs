using AniMan.Core.Domain;
using AniMan.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Infrastructure.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CachedAnime> Anime => Set<CachedAnime>();
    public DbSet<CachedEpisode> Episodes => Set<CachedEpisode>();
    public DbSet<CachedAnimeStreaming> AnimeStreaming => Set<CachedAnimeStreaming>();
    public DbSet<CachedManga> Manga => Set<CachedManga>();
    public DbSet<CachedGenre> Genres => Set<CachedGenre>();
    public DbSet<CachedMediaGenre> MediaGenres => Set<CachedMediaGenre>();

    /// <summary>Genre names linked to one anime or manga (one query for every reader, AM-088).</summary>
    public IQueryable<string> GenreNamesOf(int malId, string mediaType) => MediaGenres
        .Where(mg => mg.MediaId == malId && mg.MediaType == mediaType)
        .Join(Genres, mg => mg.GenreId, g => g.Id, (_, g) => g.Name);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        UtcDateTimeValueConverter.Register(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CachedAnime>(e =>
        {
            e.HasKey(a => a.Id);
            // Rows cached before extended metadata existed read as Legacy and get refreshed.
            e.Property(a => a.DataVersion).HasDefaultValue(MediaDataVersion.Legacy);
            e.HasMany(a => a.Episodes).WithOne(ep => ep.Anime).HasForeignKey(ep => ep.AnimeId);
            e.HasMany(a => a.StreamingPlatforms).WithOne(s => s.Anime).HasForeignKey(s => s.AnimeId);
        });

        modelBuilder.Entity<CachedEpisode>(e =>
        {
            e.HasKey(ep => ep.Id);
            e.HasIndex(ep => new { ep.AnimeId, ep.EpisodeNumber }).IsUnique();
        });

        modelBuilder.Entity<CachedAnimeStreaming>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.AnimeId, s.PlatformName }).IsUnique();
        });

        modelBuilder.Entity<CachedManga>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.DataVersion).HasDefaultValue(MediaDataVersion.Legacy);
        });

        modelBuilder.Entity<CachedGenre>(e => e.HasKey(g => g.Id));

        // Links point at anime or manga by (MediaId, MediaType), so there is no FK to either
        // table; the old unused MediaGenres navigations only added two always-null columns (AM-087).
        modelBuilder.Entity<CachedMediaGenre>(e =>
        {
            e.HasKey(mg => new { mg.MediaId, mg.MediaType, mg.GenreId });
            e.HasOne(mg => mg.Genre).WithMany(g => g.MediaGenres).HasForeignKey(mg => mg.GenreId);
        });
    }
}
