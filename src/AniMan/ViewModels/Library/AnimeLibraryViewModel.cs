using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;

namespace AniMan.ViewModels.Library;

public sealed class AnimeLibraryViewModel(ITrackingService tracking)
    : LibraryViewModelBase(tracking, MediaType.Anime)
{
    protected override (int Watched, int? Total) GetProgress(LibraryItem item) =>
        (item.EpisodeProgress?.Count(e => e.IsWatched) ?? 0, item.Snapshot?.TotalEpisodes);
}
