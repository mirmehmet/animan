using AniMan.Core.Common;

namespace AniMan.Core.Interfaces;

public interface IDataManagementService
{
    Task<Result> ResetAllDataAsync(CancellationToken ct = default);
}
