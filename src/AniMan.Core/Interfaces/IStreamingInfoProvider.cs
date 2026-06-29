using AniMan.Core.Common;

namespace AniMan.Core.Interfaces;

public record StreamingPlatform(string Name, string? Url);

public interface IStreamingInfoProvider
{
    Task<Result<IReadOnlyList<StreamingPlatform>>> GetStreamingPlatformsAsync(int malId, CancellationToken ct = default);
}
