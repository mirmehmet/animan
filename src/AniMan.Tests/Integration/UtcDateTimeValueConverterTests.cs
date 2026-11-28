using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>AM-090: stored times come back as UTC, and a local time is stored as UTC.</summary>
public sealed class UtcDateTimeValueConverterTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _factory = new(opts => new LibraryDbContext(opts));

    [Fact]
    public async Task StoredTimes_ReadBackAsUtc_AndLocalTimesAreConvertedOnWrite()
    {
        var utc = new DateTime(2026, 11, 28, 9, 15, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 1, MediaType = MediaType.Anime, StatusId = TrackingStatusIds.Watching,
                AddedAt = local,                                          // Local → stored as UTC
                UpdatedAt = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), // taken as UTC
                DeletedAt = utc
            });
            await db.SaveChangesAsync();
        }

        await using var verify = await _factory.CreateDbContextAsync();
        var item = await verify.LibraryItems.AsNoTracking().SingleAsync();

        item.AddedAt.Should().Be(utc);
        item.AddedAt.Kind.Should().Be(DateTimeKind.Utc);
        item.UpdatedAt.Should().Be(utc);
        item.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        item.DeletedAt!.Value.Kind.Should().Be(DateTimeKind.Utc, "nullable times are converted too");
    }

    [Fact]
    public async Task QueriesComparingTimes_StillRunInTheDatabase()
    {
        var utc = new DateTime(2026, 11, 28, 9, 15, 0, DateTimeKind.Utc);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 2, MediaType = MediaType.Manga, StatusId = TrackingStatusIds.Reading,
                AddedAt = utc, UpdatedAt = utc, DeletedAt = utc
            });
            await db.SaveChangesAsync();
        }

        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.LibraryItems.CountAsync(i => i.DeletedAt < utc.AddMinutes(1))).Should().Be(1);
        (await verify.LibraryItems.CountAsync(i => i.DeletedAt < utc.AddMinutes(-1))).Should().Be(0);
    }

    public void Dispose() => _factory.Dispose();
}
