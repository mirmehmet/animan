using System.Text.Json.Nodes;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Integration;

/// <summary>
/// Import failure paths over real SQLite (the InMemory provider can't represent
/// them): the Overwrite wipe must roll back when the insert phase fails, and a
/// malformed item must be skipped without aborting the rest of the import.
/// </summary>
public class ImportAtomicityTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"AniMan_test_{Guid.NewGuid():N}.json");
    private readonly SqliteContextFactory<LibraryDbContext> _factory = new(o => new LibraryDbContext(o));
    private readonly StoragePaths _paths;

    public ImportAtomicityTests()
    {
        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);
    }

    private ExportService Create()
    {
        // No real HTTP in tests — downloads fail gracefully, leaving covers null.
        var coverStore = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == new HttpClient()),
            _paths, NullLogger<CoverStore>.Instance);

        return new ExportService(_factory, coverStore, NullLogger<ExportService>.Instance);
    }

    private void SeedItem(int malId, MediaType type, int statusId)
    {
        using var db = _factory.CreateDbContext();
        db.LibraryItems.Add(new LibraryItem
        {
            MalId = malId, MediaType = type, StatusId = statusId,
            AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Snapshot = new MediaSnapshot { Title = $"Title {malId}", Genres = "[]", SnapshotAt = DateTime.UtcNow }
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Import_Overwrite_FailingInsert_PreservesExistingLibrary()
    {
        SeedItem(100, MediaType.Anime, statusId: 1);
        var svc = Create();
        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();

        // Corrupt the backup: duplicate the single item so the second insert
        // violates the unique (MalId, MediaType) index mid-import.
        var root = JsonNode.Parse(await File.ReadAllTextAsync(_tempFile))!;
        var items = root["items"]!.AsArray();
        items.Add(items[0]!.DeepClone());
        await File.WriteAllTextAsync(_tempFile, root.ToJsonString());

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeFalse();
        using var verify = _factory.CreateDbContext();
        verify.LibraryItems.Single().MalId.Should().Be(100, "the Overwrite wipe must roll back with the failed insert");
    }

    [Fact]
    public async Task Import_UnknownMediaType_SkipsBadItem_ImportsRest()
    {
        SeedItem(100, MediaType.Anime, statusId: 1);
        SeedItem(200, MediaType.Manga, statusId: 2);
        var svc = Create();
        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(_tempFile))!;
        var badItem = root["items"]!.AsArray().First(i => (int)i!["malId"]! == 200)!;
        badItem["mediaType"] = "NotAMediaType";
        await File.WriteAllTextAsync(_tempFile, root.ToJsonString());

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = _factory.CreateDbContext();
        verify.LibraryItems.Select(i => i.MalId).Should().BeEquivalentTo([100]);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        if (Directory.Exists(_paths.CoversPath))
            Directory.Delete(_paths.CoversPath, recursive: true);
    }
}
