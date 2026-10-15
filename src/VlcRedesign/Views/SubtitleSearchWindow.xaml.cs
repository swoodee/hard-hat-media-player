using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using VlcRedesign.Services;
using VlcRedesign.ViewModels;

namespace VlcRedesign.Views;

/// <summary>VLsub-style search: by file hash or by name, pick a result, download and load it.</summary>
public partial class SubtitleSearchWindow : Window
{
    private readonly SubtitleService _subs;
    private readonly PlayerViewModel _player;
    private readonly LibraryStore _store;
    private readonly string _videoPath;
    private CancellationTokenSource? _cts;

    public SubtitleSearchWindow(Window owner, SubtitleService subs, PlayerViewModel player, LibraryStore store, string videoPath)
    {
        InitializeComponent();
        Owner = owner;
        _subs = subs;
        _player = player;
        _store = store;
        _videoPath = videoPath;

        QueryBox.Text = SubtitleService.GuessQuery(videoPath);
        LanguagesBox.Text = subs.Settings.Languages;
        ApiKeyBox.Text = subs.Settings.ApiKey;
        UserBox.Text = subs.Settings.Username;
        PasswordBox.Password = Secret.Unprotect(subs.Settings.PasswordProtected) ?? "";
        AutoBox.IsChecked = subs.Settings.AutoDownload;

        if (!subs.IsConfigured)
        {
            AccountPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Add your API key to start searching.";
        }
        Loaded += (_, _) => (subs.IsConfigured ? (Control)QueryBox : ApiKeyBox).Focus();
        Closed += (_, _) => _cts?.Cancel();
    }

    private void OnSearchHash(object sender, RoutedEventArgs e) => Search(byHash: true);
    private void OnSearchName(object sender, RoutedEventArgs e) => Search(byHash: false);

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Search(byHash: false);
    }

    private async void Search(bool byHash)
    {
        SaveLanguages();
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        SetBusy(true, byHash ? "Searching for subtitles made for this file…" : $"Searching for “{QueryBox.Text.Trim()}”…");
        Results.ItemsSource = null;
        try
        {
            var results = await _subs.SearchAsync(_videoPath, QueryBox.Text, byHash, cts.Token);
            if (cts.IsCancellationRequested) return;
            var languages = _subs.Settings.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Results.ItemsSource = results
                .OrderByDescending(r => r.HashMatch)
                .ThenBy(r => languages.IndexOf(r.Language) is var i && i >= 0 ? i : int.MaxValue)
                .ThenByDescending(r => r.Downloads)
                .ToList();
            StatusText.Text = results.Count switch
            {
                0 when byHash => "No subtitles are linked to this exact file. Try searching by name.",
                0 => "Nothing found. Try a shorter title, or add more languages.",
                1 => "1 subtitle found.",
                _ => $"{results.Count} subtitles found.",
            };
            if (results.Count > 0) Results.SelectedIndex = 0;
        }
        catch (OpenSubtitlesException ex)
        {
            StatusText.Text = ex.Message;
            if (!_subs.IsConfigured) AccountPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_cts == cts) SetBusy(false, null);
        }
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not SubtitleResult result) return;
        var cts = _cts = new CancellationTokenSource();
        SetBusy(true, "Downloading…");
        try
        {
            var (path, remaining) = await _subs.DownloadAsync(result, _videoPath, cts.Token);
            if (string.Equals(_player.CurrentPath, _videoPath, StringComparison.OrdinalIgnoreCase))
                _player.AddSubtitleFile(path);
            _player.ShowMessage($"Subtitles added: {result.Language} — {System.IO.Path.GetFileName(path)}");
            StatusText.Text = remaining is { } r ? $"Saved next to the video. {r} downloads left today." : "Saved next to the video.";
            Close();
        }
        catch (OpenSubtitlesException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_cts == cts && IsLoaded) SetBusy(false, null);
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem != null) OnDownload(sender, e);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DownloadButton.IsEnabled = Results.SelectedItem != null && HashButton.IsEnabled;

    private void OnToggleAccount(object sender, RoutedEventArgs e) =>
        AccountPanel.Visibility = AccountPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void OnSaveAccount(object sender, RoutedEventArgs e)
    {
        var s = _subs.Settings;
        s.ApiKey = ApiKeyBox.Text.Trim();
        s.Username = UserBox.Text.Trim();
        s.PasswordProtected = Secret.Protect(PasswordBox.Password);
        s.AutoDownload = AutoBox.IsChecked == true;
        SaveLanguages();
        _subs.Client.Reset();
        _store.Save();
        AccountPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = s.ApiKey.Length > 0 ? "Saved. You can search now." : "Saved. Searching needs an API key.";
    }

    private void SaveLanguages()
    {
        var langs = string.Join(",", LanguagesBox.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().ToLowerInvariant()).Distinct());
        if (langs.Length == 0 || langs == _subs.Settings.Languages) return;
        _subs.Settings.Languages = langs;
        _store.Save();
    }

    private void SetBusy(bool busy, string? status)
    {
        HashButton.IsEnabled = NameButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy && Results.SelectedItem != null;
        Cursor = busy ? Cursors.AppStarting : null;
        if (status != null) StatusText.Text = status;
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
