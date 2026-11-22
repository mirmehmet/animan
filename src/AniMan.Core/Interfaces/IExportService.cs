using AniMan.Core.Common;

namespace AniMan.Core.Interfaces;

public enum ImportMode { Merge, Overwrite }

/// <summary>Import phases, in order. Cover downloads usually dominate the duration.</summary>
public enum ImportStage { Reading, Saving, DownloadingCovers }

/// <param name="Stage">Current phase.</param>
/// <param name="Fraction">Overall completion, 0–1, never decreasing.</param>
/// <param name="Done">Items processed in this phase (0 when the phase has no count).</param>
/// <param name="Total">Items in this phase (0 when the phase has no count).</param>
public sealed record ImportProgress(ImportStage Stage, double Fraction, int Done = 0, int Total = 0);

/// <param name="Imported">Items written to the library.</param>
/// <param name="Skipped">Items left out: already in the library, duplicated in the file, or invalid.</param>
/// <param name="CoversFailed">Covers that could not be downloaded (the item is imported without one).</param>
public sealed record ImportSummary(int Imported, int Skipped, int CoversFailed);

public interface IExportService
{
    Task<Result> ExportAsync(string filePath, CancellationToken ct = default);

    Task<Result<ImportSummary>> ImportAsync(
        string filePath, ImportMode mode,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
