using AniMan.Core.Common;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

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
            await using var lib = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            lib.LibraryItems.RemoveRange(lib.LibraryItems);
            lib.Settings.RemoveRange(lib.Settings);
            await lib.SaveChangesAsync(ct).ConfigureAwait(false);

            await using var cat = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            cat.AnimeStreaming.RemoveRange(cat.AnimeStreaming);
            cat.Episodes.RemoveRange(cat.Episodes);
            cat.MediaGenres.RemoveRange(cat.MediaGenres);
            cat.Genres.RemoveRange(cat.Genres);
            cat.Anime.RemoveRange(cat.Anime);
            cat.Manga.RemoveRange(cat.Manga);
            await cat.SaveChangesAsync(ct).ConfigureAwait(false);

            if (Directory.Exists(storagePaths.CoversPath))
                foreach (var f in Directory.GetFiles(storagePaths.CoversPath))
                    File.Delete(f);

            logger.LogInformation("Full data reset completed");
            return Result.Success();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Full data reset failed");
            return Result.Failure("Failed to reset data. Check the log file for details.");
        }
    }
}
