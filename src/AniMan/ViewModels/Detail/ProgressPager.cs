using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Detail;

/// <summary>
/// Shows a long episode/chapter list a hundred rows at a time, with "1–100, 101–200 …" pills
/// to switch. The page sits in one outer scroll view, so its list cannot virtualize; drawing
/// only one range keeps a 1,000-episode series as quick to open as a short one. Lists of up
/// to <see cref="PageSize"/> rows show whole, with no pills.
/// </summary>
public sealed partial class ProgressPager<TRow> : ObservableObject where TRow : class
{
    public const int PageSize = 100;

    private readonly Func<TRow, int> _number;
    private readonly Func<TRow, bool> _isDone;
    private IReadOnlyList<TRow> _rows = [];

    /// <param name="number">Episode/chapter number of a row.</param>
    /// <param name="isDone">Whether a row is watched/read; the list opens on the first range with an undone row.</param>
    public ProgressPager(Func<TRow, int> number, Func<TRow, bool> isDone)
    {
        _number = number;
        _isDone = isDone;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRanges))]
    private IReadOnlyList<ProgressRange> _ranges = [];

    /// <summary>Rows of the selected range (all rows when there is only one range).</summary>
    [ObservableProperty]
    private IReadOnlyList<TRow> _visible = [];

    public bool HasRanges => Ranges.Count > 1;

    /// <summary>Takes a freshly built row list and opens it where the user left off.</summary>
    public void Reset(IReadOnlyList<TRow> rows)
    {
        _rows = rows;
        var ranges = new List<ProgressRange>();
        for (var start = 0; start < rows.Count; start += PageSize)
        {
            var end = Math.Min(start + PageSize, rows.Count) - 1;
            ranges.Add(new ProgressRange(start, end, _number(rows[start]), _number(rows[end])));
        }
        Ranges = ranges;

        var firstUndone = rows.ToList().FindIndex(r => !_isDone(r));
        Select(ranges.Count == 0 ? null : ranges[Math.Max(firstUndone, 0) / PageSize]);
    }

    [RelayCommand]
    private void Select(ProgressRange? range)
    {
        foreach (var r in Ranges)
            r.IsSelected = ReferenceEquals(r, range);

        Visible = range is null
            ? []
            : _rows.Skip(range.FirstIndex).Take(range.LastIndex - range.FirstIndex + 1).ToList();
    }
}

/// <summary>One pill: rows <see cref="FirstIndex"/>..<see cref="LastIndex"/> of the list.</summary>
public sealed partial class ProgressRange(int firstIndex, int lastIndex, int firstNumber, int lastNumber)
    : ObservableObject
{
    public int FirstIndex { get; } = firstIndex;
    public int LastIndex { get; } = lastIndex;
    public string Label { get; } = $"{firstNumber}–{lastNumber}";

    [ObservableProperty]
    private bool _isSelected;
}
