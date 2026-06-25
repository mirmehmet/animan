using AniTrack.Core.Common;

namespace AniTrack.Core.Interfaces;

public enum ImportMode { Merge, Overwrite }

public interface IExportService
{
    Task<Result> ExportAsync(string filePath, CancellationToken ct = default);
    Task<Result> ImportAsync(string filePath, ImportMode mode, CancellationToken ct = default);
}
