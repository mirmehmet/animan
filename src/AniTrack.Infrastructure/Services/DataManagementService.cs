using AniTrack.Core.Common;
using AniTrack.Core.Interfaces;
using AniTrack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniTrack.Infrastructure.Services;

public sealed class DataManagementService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    IDbContextFactory<CatalogDbContext> catalogFactory,
    StoragePaths storagePaths,
    ILogger<DataManagementService> logger) : IDataManagementService
{
    public async Task<Result> ResetAllDataAsync(CancellationToken ct = default)
    {
        try
        {
            await using var lib = await libraryFactory.CreateDbContextAsync(ct);
            lib.LibraryItems.RemoveRange(lib.LibraryItems);
            lib.Settings.RemoveRange(lib.Settings);
            await lib.SaveChangesAsync(ct);

            await using var cat = await catalogFactory.CreateDbContextAsync(ct);
            cat.AnimeStreaming.RemoveRange(cat.AnimeStreaming);
            cat.Episodes.RemoveRange(cat.Episodes);
            cat.MediaGenres.RemoveRange(cat.MediaGenres);
            cat.Genres.RemoveRange(cat.Genres);
            cat.Anime.RemoveRange(cat.Anime);
            cat.Manga.RemoveRange(cat.Manga);
            await cat.SaveChangesAsync(ct);

            if (Directory.Exists(storagePaths.CoversPath))
                foreach (var f in Directory.GetFiles(storagePaths.CoversPath))
                    File.Delete(f);

            logger.LogInformation("Full data reset completed");
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Full data reset failed");
            return Result.Failure(ex.Message);
        }
    }
}
