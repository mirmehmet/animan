using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>Full CRUD round-trip against real (in-memory) SQLite, including cascade delete.</summary>
public class LibraryDbContextIntegrationTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _factory =
        new(opts => new LibraryDbContext(opts));

    [Fact]
    public async Task Create_Read_Update_Delete_RoundTrip_WithCascade()
    {
        // ── Create ──────────────────────────────────────────────────────────────
        await using (var db = _factory.CreateDbContext())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 1, MediaType = MediaType.Anime, StatusId = 1,
                Score = 9, AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Snapshot = new MediaSnapshot { Title = "Cowboy Bebop", TotalEpisodes = 26, Genres = "[]", SnapshotAt = DateTime.UtcNow },
                EpisodeProgress = { new EpisodeProgress { EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow } },
                Notes = { new Note { Content = "great", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow } },
                StreamingOverrides = { new UserStreamingOverride { PlatformName = "Netflix", AddedAt = DateTime.UtcNow } }
            });
            await db.SaveChangesAsync();
        }

        int itemId;

        // ── Read (fresh context, navigation includes) ────────────────────────────
        await using (var db = _factory.CreateDbContext())
        {
            var item = await db.LibraryItems
                .Include(i => i.Snapshot)
                .Include(i => i.EpisodeProgress)
                .Include(i => i.Notes)
                .Include(i => i.StreamingOverrides)
                .Include(i => i.Status)
                .SingleAsync();

            itemId = item.Id;
            item.Score.Should().Be(9);
            item.Status!.Name.Should().Be("Watching");
            item.Snapshot!.Title.Should().Be("Cowboy Bebop");
            item.EpisodeProgress.Should().ContainSingle();
            item.Notes.Should().ContainSingle();
            item.StreamingOverrides.Should().ContainSingle();
        }

        // ── Update ───────────────────────────────────────────────────────────────
        await using (var db = _factory.CreateDbContext())
        {
            var item = await db.LibraryItems.SingleAsync();
            item.Score = 7;
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateDbContext())
            (await db.LibraryItems.SingleAsync()).Score.Should().Be(7);

        // ── Delete (cascade removes dependents) ──────────────────────────────────
        await using (var db = _factory.CreateDbContext())
        {
            var item = await db.LibraryItems.SingleAsync();
            db.LibraryItems.Remove(item);
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            db.LibraryItems.Should().BeEmpty();
            (await db.Snapshots.CountAsync(s => s.LibraryItemId == itemId)).Should().Be(0);
            (await db.EpisodeProgress.CountAsync(e => e.LibraryItemId == itemId)).Should().Be(0);
            (await db.Notes.CountAsync(n => n.LibraryItemId == itemId)).Should().Be(0);
            (await db.StreamingOverrides.CountAsync(o => o.LibraryItemId == itemId)).Should().Be(0);
        }
    }

    [Fact]
    public async Task DuplicateMalIdAndMediaType_ViolatesUniqueIndex()
    {
        await using var db = _factory.CreateDbContext();
        db.LibraryItems.Add(new LibraryItem { MalId = 5, MediaType = MediaType.Anime, StatusId = 1, AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.LibraryItems.Add(new LibraryItem { MalId = 5, MediaType = MediaType.Anime, StatusId = 1, AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public void DefaultStatuses_AreSeeded()
    {
        using var db = _factory.CreateDbContext();
        db.TrackingStatuses.Should().HaveCount(7);
        db.TrackingStatuses.Select(s => s.Name).Should().Contain(["Watching", "Completed", "Plan to watch"]);
    }

    public void Dispose() => _factory.Dispose();
}
