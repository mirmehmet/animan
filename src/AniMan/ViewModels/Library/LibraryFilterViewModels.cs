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

/// <summary>A labelled row of chips (Format, Genres, …); hidden when the library has no values for it.</summary>
public sealed class FilterGroupViewModel(string header)
{
    public string Header { get; } = header;
    public ObservableCollection<FilterChipViewModel> Chips { get; } = [];
    public bool HasChips => Chips.Count > 0;

    public IEnumerable<string> SelectedValues => Chips.Where(c => c.IsSelected).Select(c => c.Value);
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
