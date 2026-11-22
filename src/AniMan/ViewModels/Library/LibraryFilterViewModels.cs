using System.Collections.ObjectModel;
using AniMan.Core.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Library;

/// <summary>One selectable value in the filter panel, e.g. "Action (42)".</summary>
public sealed partial class FilterChipViewModel(string value, string label, int count, Action onChanged)
    : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;
    public int Count { get; } = count;
    public string Display => $"{Label} ({Count})";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged();
}

/// <summary>
/// A labelled row of chips (Format, Genres, …); hidden when the library has no values for it.
/// Groups that support it (genres, themes) can switch between "has all selected" and
/// "has any selected"; the switch only shows once two or more chips are selected, since
/// with a single selection both modes give the same result.
/// </summary>
public sealed partial class FilterGroupViewModel : ObservableObject
{
    private readonly Action? _onMatchModeChanged;

    public FilterGroupViewModel(string header, bool supportsMatchMode = false, Action? onMatchModeChanged = null)
    {
        Header = header;
        SupportsMatchMode = supportsMatchMode;
        _onMatchModeChanged = onMatchModeChanged;
        Chips.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChips));
    }

    public string Header { get; }
    public ObservableCollection<FilterChipViewModel> Chips { get; } = [];
    public bool HasChips => Chips.Count > 0;

    public bool SupportsMatchMode { get; }

    /// <summary>False (default): items need every selected value. True: any one is enough.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchAll))]
    private bool _matchAny;

    public bool MatchAll => !MatchAny;

    public bool ShowMatchMode => SupportsMatchMode && Chips.Count(c => c.IsSelected) >= 2;

    public IEnumerable<string> SelectedValues => Chips.Where(c => c.IsSelected).Select(c => c.Value);

    /// <summary>Called after any selection change so <see cref="ShowMatchMode"/> re-evaluates.</summary>
    public void RefreshSelectionState() => OnPropertyChanged(nameof(ShowMatchMode));

    partial void OnMatchAnyChanged(bool value) => _onMatchModeChanged?.Invoke();

    [RelayCommand]
    private void SetMatchAll() => MatchAny = false;

    [RelayCommand]
    private void SetMatchAny() => MatchAny = true;
}

/// <summary>A removable summary chip shown under the tabs while the panel is closed.</summary>
public sealed partial class ActiveFilterViewModel(string label, Action remove)
{
    public string Label { get; } = label;

    [RelayCommand]
    private void Remove() => remove();
}

public sealed record SortOption(LibrarySort Value, string Label);

public sealed record RatingOption(RatingFilter Value, string Label);

/// <summary>Year dropdown entry; <see cref="Value"/> 0 means "any".</summary>
public sealed record YearOption(int Value, string Label);
