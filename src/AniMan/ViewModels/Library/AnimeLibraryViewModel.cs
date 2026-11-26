using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;

namespace AniMan.ViewModels.Library;

public sealed class AnimeLibraryViewModel(
    ITrackingService tracking, ISettingsService settings, LibraryLayoutViewModel layout, ILibraryVersion libraryVersion)
    : LibraryViewModelBase(tracking, settings, layout, libraryVersion, MediaType.Anime)
{
    protected override int? GetTotal(MediaSnapshot? snapshot) => snapshot?.TotalEpisodes;
}
