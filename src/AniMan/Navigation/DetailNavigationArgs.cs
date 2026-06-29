using AniMan.Core.Domain.Enums;

namespace AniMan.Navigation;

public sealed record DetailNavigationArgs(
    int MalId,
    MediaType MediaType,
    int? LibraryItemId,
    string BackLabel
);
