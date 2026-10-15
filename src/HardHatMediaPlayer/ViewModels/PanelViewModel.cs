using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardHatMediaPlayer.Services;

namespace HardHatMediaPlayer.ViewModels;

/// <summary>An entry in a context menu. Label "-" is a separator.</summary>
public sealed record MenuAction(string Label, Action? Run = null, IReadOnlyList<MenuAction>? Children = null, bool IsChecked = false, string? Gesture = null)
{
    public static readonly MenuAction Separator = new("-");
    public bool IsSeparator => Label == "-";
}

public abstract class PanelRow : ObservableObject
{
    public virtual bool IsSection => false;

    /// <summary>What clicking the row does.</summary>
    public abstract void Activate();
}

/// <summary>Bold heading inside a page, e.g. "Continue Watching ›".</summary>
public sealed class SectionRow : PanelRow
{
    public override bool IsSection => true;
    public string Title { get; init; } = "";
    public Action? Open { get; init; }
    public bool HasOpen => Open != null;

    public override void Activate() => Open?.Invoke();
}

public sealed class ItemRow : PanelRow
{
    private bool _isCurrent;

    public string Label { get; init; } = "";
    public string? Detail { get; init; }
    public Geometry? Icon { get; init; }
    public string? Thumbnail { get; init; }
    public bool IsAction { get; init; }
    public Action? Invoke { get; init; }
    public Func<IReadOnlyList<MenuAction>>? Menu { get; init; }
    public string? MediaPath { get; init; }

    public bool HasThumbnail => Thumbnail != null;

    public override void Activate() => Invoke?.Invoke();

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

public sealed class PanelPage
{
    /// <summary>Set on the rail's top-level pages ("root:music"), so they can be told apart from folders with the same name.</summary>
    public string? Id { get; init; }
    public string Title { get; init; } = "";
    public string RailKey { get; init; } = "";
    public List<PanelRow> Rows { get; init; } = new();
    public bool CanSort { get; init; } = true;
    public string? EmptyText { get; init; }
    public Func<IReadOnlyList<MenuAction>>? Menu { get; init; }
}

/// <summary>The library side panel: page navigation with back/forward, search and sort.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly List<Func<PanelPage>> _back = new();
    private readonly List<Func<PanelPage>> _forward = new();
    private Func<PanelPage>? _current;
    private string? _currentMediaPath;

    [ObservableProperty] private PanelPage _page = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private int _sortMode;
    [ObservableProperty] private bool _showDetails = true;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _emptyText = "";

    /// <summary>Replaced as a whole (one change notification) rather than edited row by row.</summary>
    [ObservableProperty] private IReadOnlyList<PanelRow> _rows = Array.Empty<PanelRow>();

    private bool _suppressApply;

    /// <summary>Raised around in-place refreshes so the view can keep its scroll position.</summary>
    public event Action? Refreshing;
    public event Action? Refreshed;

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public string SortLabel => SortMode switch { 1 => "Sorted A–Z", 2 => "Sorted Z–A", _ => "Default order" };

    public event Action<string>? RailChanged;

    public void Navigate(Func<PanelPage> factory)
    {
        if (_current != null) _back.Add(_current);
        _forward.Clear();
        Show(factory);
    }

    /// <summary>Rebuilds the current page in place (after the library changed).</summary>
    public void Refresh()
    {
        if (_current == null) return;
        Refreshing?.Invoke();
        Page = _current();
        Apply();
        Refreshed?.Invoke();
    }

    public string? CurrentMediaPath
    {
        get => _currentMediaPath;
        set
        {
            _currentMediaPath = value;
            foreach (var row in Rows.OfType<ItemRow>())
                row.IsCurrent = row.MediaPath != null && string.Equals(row.MediaPath, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (_back.Count == 0 || _current == null) return;
        _forward.Add(_current);
        var f = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Show(f);
    }

    [RelayCommand]
    private void Forward()
    {
        if (_forward.Count == 0 || _current == null) return;
        _back.Add(_current);
        var f = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Show(f);
    }

    [RelayCommand]
    private void CycleSort() => SortMode = (SortMode + 1) % 3;

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchOpen = !IsSearchOpen;
        if (!IsSearchOpen) SearchText = "";
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressApply) Apply();
    }

    partial void OnSortModeChanged(int value)
    {
        OnPropertyChanged(nameof(SortLabel));
        Apply();
    }

    private void Show(Func<PanelPage> factory)
    {
        _current = factory;
        _suppressApply = true;
        IsSearchOpen = false;
        SearchText = "";
        _suppressApply = false;
        Page = factory();
        Apply();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        RailChanged?.Invoke(Page.RailKey);
    }

    private void Apply()
    {
        IEnumerable<PanelRow> rows = Page.Rows;
        var query = SearchText.Trim();

        if (query.Length > 0)
            rows = rows.OfType<ItemRow>().Where(r => !r.IsAction && r.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        if (Page.CanSort && SortMode != 0 && !rows.Any(r => r is SectionRow))
        {
            var list = rows.ToList();
            var actions = list.Where(r => r is ItemRow { IsAction: true });
            var items = list.OfType<ItemRow>().Where(r => !r.IsAction);
            items = SortMode == 1
                ? items.OrderBy(r => r.Label, NaturalComparer.Instance)
                : items.OrderByDescending(r => r.Label, NaturalComparer.Instance);
            rows = actions.Concat(items);
        }

        Rows = rows.ToList();
        CurrentMediaPath = _currentMediaPath;

        var count = Rows.OfType<ItemRow>().Count(r => !r.IsAction);
        // A page made only of shortcuts (e.g. Home before anything was played) has nothing to count.
        var onlyActions = count == 0 && Rows.OfType<ItemRow>().Any();
        CountText = onlyActions ? "" : count == 1 ? "1 item" : $"{count} items";
        IsEmpty = count == 0;
        EmptyText = query.Length > 0 ? $"Nothing matches “{query}”" : Page.EmptyText ?? "";
    }
}
