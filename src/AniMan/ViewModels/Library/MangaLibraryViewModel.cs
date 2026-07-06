using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;

namespace AniMan.ViewModels.Library;

public sealed class MangaLibraryViewModel(ITrackingService tracking)
    : LibraryViewModelBase(tracking, MediaType.Manga)
{
    protected override (int Watched, int? Total) GetProgress(LibraryItem item) =>
        (item.ChapterProgress?.Count(c => c.IsRead) ?? 0, item.Snapshot?.TotalChapters);
}
