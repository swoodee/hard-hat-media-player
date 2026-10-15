using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HardHatMediaPlayer.Services;

namespace HardHatMediaPlayer.ViewModels;

/// <summary>One entry in the play queue. The same file can be queued more than once; each entry is its own item.</summary>
public sealed partial class QueueItem : ObservableObject
{
    public QueueItem(string path, string title, long durationMs)
    {
        Path = path;
        Kind = MediaScanner.KindOf(path);
        _title = title;
        _durationMs = durationMs;
    }

    public string Path { get; }
    public MediaKind Kind { get; }

    public Geometry? Icon => Application.Current?.TryFindResource(Kind switch
    {
        MediaKind.Audio => "I.Note",
        MediaKind.Stream => "I.Globe",
        _ => "I.Film",
    }) as Geometry;

    [ObservableProperty] private string _title;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Duration))] private long _durationMs;
    public string? Duration => DurationMs > 0 ? PlayerViewModel.Format(DurationMs) : null;
    [ObservableProperty] private bool _isCurrent;

    /// <summary>Plays this entry (clicking the row, or Invoke from UI Automation).</summary>
    internal Action? Play { get; set; }
}

/// <summary>The queue's items; replacing them all raises a single Reset instead of one event per item.</summary>
public sealed class QueueCollection : ObservableCollection<QueueItem>
{
    public void ReplaceAll(IEnumerable<QueueItem> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
