using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Infrastructure.Data;

public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<TrackingStatus> TrackingStatuses => Set<TrackingStatus>();
    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();
    public DbSet<MediaSnapshot> Snapshots => Set<MediaSnapshot>();
    public DbSet<EpisodeProgress> EpisodeProgress => Set<EpisodeProgress>();
    public DbSet<ChapterProgress> ChapterProgress => Set<ChapterProgress>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<UserStreamingOverride> StreamingOverrides => Set<UserStreamingOverride>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackingStatus>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Name).IsUnique();
        });

        modelBuilder.Entity<LibraryItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => new { i.MalId, i.MediaType }).IsUnique();
            e.Property(i => i.MediaType).HasConversion<string>();
            e.HasOne(i => i.Status).WithMany(s => s.LibraryItems).HasForeignKey(i => i.StatusId);
            e.HasOne(i => i.Snapshot).WithOne(s => s.LibraryItem).HasForeignKey<MediaSnapshot>(s => s.LibraryItemId);
            e.HasMany(i => i.EpisodeProgress).WithOne(ep => ep.LibraryItem).HasForeignKey(ep => ep.LibraryItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.ChapterProgress).WithOne(cp => cp.LibraryItem).HasForeignKey(cp => cp.LibraryItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Notes).WithOne(n => n.LibraryItem).HasForeignKey(n => n.LibraryItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.StreamingOverrides).WithOne(o => o.LibraryItem).HasForeignKey(o => o.LibraryItemId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaSnapshot>(e => e.HasKey(s => s.LibraryItemId));

        modelBuilder.Entity<EpisodeProgress>(e =>
        {
            e.HasKey(ep => ep.Id);
            e.HasIndex(ep => new { ep.LibraryItemId, ep.EpisodeNumber }).IsUnique();
        });

        modelBuilder.Entity<ChapterProgress>(e =>
        {
            e.HasKey(cp => cp.Id);
            e.HasIndex(cp => new { cp.LibraryItemId, cp.ChapterNumber }).IsUnique();
        });

        modelBuilder.Entity<Note>(e => e.HasKey(n => n.Id));

        modelBuilder.Entity<UserStreamingOverride>(e => e.HasKey(o => o.Id));

        modelBuilder.Entity<AppSetting>(e => e.HasKey(s => s.Key));

        SeedDefaultStatuses(modelBuilder);
    }

    private static void SeedDefaultStatuses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackingStatus>().HasData(
            new TrackingStatus { Id = TrackingStatusIds.Watching, Name = "Watching", IsDefault = true, DisplayOrder = 1 },
            new TrackingStatus { Id = TrackingStatusIds.Reading, Name = "Reading", IsDefault = true, DisplayOrder = 1 },
            new TrackingStatus { Id = TrackingStatusIds.Completed, Name = "Completed", IsDefault = true, DisplayOrder = 2 },
            new TrackingStatus { Id = TrackingStatusIds.OnHold, Name = "On-hold", IsDefault = true, DisplayOrder = 3 },
            new TrackingStatus { Id = TrackingStatusIds.Dropped, Name = "Dropped", IsDefault = true, DisplayOrder = 4 },
            new TrackingStatus { Id = TrackingStatusIds.PlanToWatch, Name = "Plan to watch", IsDefault = true, DisplayOrder = 5 },
            new TrackingStatus { Id = TrackingStatusIds.PlanToRead, Name = "Plan to read", IsDefault = true, DisplayOrder = 5 }
        );
    }
}
