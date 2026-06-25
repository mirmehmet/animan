using AniTrack.Core.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniTrack.ViewModels.Discover;

public partial class DiscoverCardViewModel : ObservableObject
{
    public int MalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public double? Score { get; init; }
    public string? Type { get; init; }
    public MediaType MediaType { get; init; }

    [ObservableProperty] private bool _isInLibrary;
}
