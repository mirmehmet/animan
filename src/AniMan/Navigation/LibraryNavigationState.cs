using AniMan.Core.Domain.Enums;

namespace AniMan.Navigation;

public static class LibraryNavigationState
{
    public static int? PendingAnimeStatusId { get; set; }
    public static int? PendingMangaStatusId { get; set; }

    public static void RequestFavorites(MediaType mediaType)
    {
        if (mediaType == MediaType.Anime)
            PendingAnimeStatusId = 99;
        else
            PendingMangaStatusId = 99;
    }
}
