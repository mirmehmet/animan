using AniTrack.Core.Common;

namespace AniTrack.Core.Interfaces;

public record StreamingPlatform(string Name, string? Url);

public interface IStreamingInfoProvider
{
    Task<Result<IReadOnlyList<StreamingPlatform>>> GetStreamingPlatformsAsync(int malId, CancellationToken ct = default);
}
