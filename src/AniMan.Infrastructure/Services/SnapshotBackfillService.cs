using AniMan.Core.Domain;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

/// <summary>
/// Quietly completes library snapshots written before extended metadata existed — after an
/// app update, or after importing an older backup. Runs once per launch in the background:
/// one item at a time, with a pause between requests so the user's own browsing keeps most
/// of the rate limit. Anything that fails stays Legacy and is retried on the next launch;
/// the user's data is never rewritten, only the new fields are filled.
/// </summary>
public sealed class SnapshotBackfillService(
    IDbContextFactory<LibraryDbContext> libraryFactory,
    ISnapshotService snapshotService,
    ILogger<SnapshotBackfillService> logger,
    IDbContextFactory<CatalogDbContext>? catalogFactory = null)
{
    /// <summary>Set once <see cref="RepairMisstampedFallbackDataAsync"/> has run (a full reset clears it; it then runs on an empty library).</summary>
    internal const string RepairDoneKey = "Repair.FallbackStampedCurrent.v1";
    /// <summary>After this many failures in a row the source is likely down or the
    /// machine offline; stop instead of burning through the whole list.</summary>
    private const int MaxConsecutiveFailures = 5;

    /// <summary>Lets the window finish loading before the first request.</summary>
    internal TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(10);

    internal TimeSpan DelayBetweenItems { get; init; } = TimeSpan.FromSeconds(1.5);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await RepairMisstampedFallbackDataAsync(ct).ConfigureAwait(false);

            List<int> pending;
            await using (var db = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                // Active items first (what the user sees), most recently touched first; trash last.
                pending = await db.Snapshots.AsNoTracking()
                    .Where(s => s.DataVersion < MediaDataVersion.Current && s.LibraryItem != null)
                    .OrderBy(s => s.LibraryItem!.DeletedAt != null)
                    .ThenByDescending(s => s.LibraryItem!.UpdatedAt)
                    .Select(s => s.LibraryItemId)
                    .ToListAsync(ct).ConfigureAwait(false);
            }

            if (pending.Count == 0)
                return;

            logger.LogInformation("Snapshot backfill: {Count} items need extended metadata", pending.Count);

            int filled = 0, failed = 0, consecutiveFailures = 0;
            foreach (var libraryItemId in pending)
            {
                var result = await snapshotService.FillExtendedMetadataAsync(libraryItemId, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    filled++;
                    consecutiveFailures = 0;
                }
                else
                {
                    failed++;
                    consecutiveFailures++;
                    logger.LogDebug("Snapshot backfill: item {Id} failed: {Error}", libraryItemId, result.Error);
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        logger.LogWarning(
                            "Snapshot backfill: {Failures} failures in a row; stopping until next launch",
                            consecutiveFailures);
                        break;
                    }
                }

                await Task.Delay(DelayBetweenItems, ct).ConfigureAwait(false);
            }

            logger.LogInformation(
                "Snapshot backfill finished: {Filled} filled, {Failed} failed, {Remaining} left for next launch",
                filled, failed, pending.Count - filled);
        }
        catch (OperationCanceledException)
        {
            // App is closing; whatever is left resumes next launch.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snapshot backfill aborted");
        }
    }

    /// <summary>
    /// One-time repair for data written before fallback rows were told apart: the fallback
    /// source has no themes or demographics at all, while the primary sends a (possibly empty)
    /// list, so a "complete" row with neither was almost certainly filled by the fallback.
    /// Only the version marker changes — no field is touched — and the backfill then completes
    /// these rows. A row marked by mistake just gets fetched once more.
    /// </summary>
    internal async Task RepairMisstampedFallbackDataAsync(CancellationToken ct)
    {
        try
        {
            await using var library = await libraryFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            if (await library.Settings.AnyAsync(s => s.Key == RepairDoneKey, ct).ConfigureAwait(false))
                return;

            var snapshots = await library.Snapshots
                .Where(s => s.DataVersion == MediaDataVersion.Current && s.Themes == null && s.Demographics == null)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.DataVersion, MediaDataVersion.Partial), ct)
                .ConfigureAwait(false);

            int anime = 0, manga = 0;
            if (catalogFactory is not null)
            {
                await using var catalog = await catalogFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                anime = await catalog.Anime
                    .Where(a => a.DataVersion == MediaDataVersion.Current && a.Themes == null && a.Demographics == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(a => a.DataVersion, MediaDataVersion.Partial), ct)
                    .ConfigureAwait(false);
                manga = await catalog.Manga
                    .Where(m => m.DataVersion == MediaDataVersion.Current && m.Themes == null && m.Demographics == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(m => m.DataVersion, MediaDataVersion.Partial), ct)
                    .ConfigureAwait(false);
            }

            library.Settings.Add(new Core.Domain.Models.AppSetting
            {
                Key = RepairDoneKey,
                Value = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            });
            await library.SaveChangesAsync(ct).ConfigureAwait(false);

            if (snapshots + anime + manga > 0)
            {
                logger.LogInformation(
                    "Marked fallback-filled data for completion: {Snapshots} snapshots, {Anime} anime, {Manga} manga",
                    snapshots, anime, manga);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Never blocks the backfill: the repair simply runs again next launch.
            logger.LogWarning(ex, "Fallback data repair failed");
        }
    }
}
