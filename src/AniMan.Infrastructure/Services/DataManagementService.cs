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
    private static readonly string[] PreservedSettingKeys =
        ["Theme", "Language", "Font", "StartupPage", "CacheRefreshDays"];

    public async Task<Result> ResetAllDataAsync(CancellationToken ct = default)
    {
        try
        {
            await using var lib = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            if (lib.Database.IsRelational())
            {
                await lib.LibraryItems.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await lib.TrackingStatuses
                    .Where(s => !s.IsDefault)
                    .ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await lib.Settings
                    .Where(s => !PreservedSettingKeys.Contains(s.Key))
                    .ExecuteDeleteAsync(ct).ConfigureAwait(false);
            }
            else
            {
                lib.LibraryItems.RemoveRange(lib.LibraryItems);
                lib.TrackingStatuses.RemoveRange(lib.TrackingStatuses.Where(s => !s.IsDefault));
                lib.Settings.RemoveRange(lib.Settings.Where(s => !PreservedSettingKeys.Contains(s.Key)));
                await lib.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await using var cat = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            if (cat.Database.IsRelational())
            {
                await cat.AnimeStreaming.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await cat.Episodes.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await cat.MediaGenres.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await cat.Genres.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await cat.Anime.ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await cat.Manga.ExecuteDeleteAsync(ct).ConfigureAwait(false);
            }
            else
            {
                cat.AnimeStreaming.RemoveRange(cat.AnimeStreaming);
                cat.Episodes.RemoveRange(cat.Episodes);
                cat.MediaGenres.RemoveRange(cat.MediaGenres);
                cat.Genres.RemoveRange(cat.Genres);
                cat.Anime.RemoveRange(cat.Anime);
                cat.Manga.RemoveRange(cat.Manga);
                await cat.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            CatalogService.ClearCaches();

            if (Directory.Exists(storagePaths.CoversPath))
            {
                foreach (var f in Directory.GetFiles(storagePaths.CoversPath))
                {
                    try
                    {
                        File.Delete(f);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Could not delete cover file {Path} during reset", f);
                    }
                }
            }

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
