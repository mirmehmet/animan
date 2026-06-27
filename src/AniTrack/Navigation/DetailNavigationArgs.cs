using AniTrack.Core.Domain.Enums;

namespace AniTrack.Navigation;

public sealed record DetailNavigationArgs(
    int MalId,
    MediaType MediaType,
    int? LibraryItemId,
    string BackLabel
);
