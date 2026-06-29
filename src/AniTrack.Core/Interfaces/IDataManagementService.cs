using AniTrack.Core.Common;

namespace AniTrack.Core.Interfaces;

public interface IDataManagementService
{
    Task<Result> ResetAllDataAsync(CancellationToken ct = default);
}
