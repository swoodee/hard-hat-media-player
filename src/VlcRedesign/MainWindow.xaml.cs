using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VlcRedesign.Controls;
using VlcRedesign.ViewModels;
using VlcRedesign.Views;

namespace VlcRedesign;

public partial class MainWindow : Window, IUiServices
{
    private const double RailWidth = 48;
    private const double MinPanelWidth = 220;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _hideControls;
    private readonly DispatcherTimer _singleClick;
    private readonly string[] _startupFiles;
    private WindowState _stateBeforeFullscreen;
    private bool _controlsVisible = true;

    public MainWindow(string[] startupFiles)
    {
        InitializeComponent();
        _startupFiles = startupFiles;
        _vm = new MainViewModel(this, Dispatcher);
        DataContext = _vm;
        Overlay.DataContext = _vm; // the overlay lives in libVLC's foreground window
        Video.MediaPlayer = _vm.Player.Player;

        _hideControls = new DispatcherTimer(TimeSpan.FromSeconds(2.5), DispatcherPriority.Normal, (_, _) => HideControls(), Dispatcher);
        _singleClick = new DispatcherTimer(TimeSpan.FromMilliseconds(230), DispatcherPriority.Normal, (_, _) =>
        {
            _singleClick!.Stop();
            _vm.Player.TogglePlay();
        }, Dispatcher);
        _singleClick.Stop();

        _vm.PropertyChanged += OnViewModelChanged;
        _vm.Player.PropertyChanged += OnPlayerChanged;
        _vm.FullscreenToggled += ToggleFullscreen;
        _vm.Panel.Refreshing += () => _scrollBeforeRefresh = ListScroller?.VerticalOffset ?? 0;
        _vm.Panel.Refreshed += () => Dispatcher.BeginInvoke(() => ListScroller?.ScrollToVerticalOffset(_scrollBeforeRefresh), DispatcherPriority.Loaded);
        StateChanged += (_, _) =>
        {
            var max = WindowState == WindowState.Maximized;
            MaxIcon.Data = (Geometry)FindResource(max ? "I.Restore" : "I.Maximize");
            MaxButton.ToolTip = max ? "Restore" : "Maximize";
        };
        SizeChanged += (_, _) => { if (_vm.IsPanelWide || PanelCol.ActualWidth > ActualWidth - RailWidth - 240) UpdateColumns(); };

        // Icons that follow player state start from the saved state, not the XAML defaults.
        foreach (var name in new[] { nameof(PlayerViewModel.IsPlaying), nameof(PlayerViewModel.Volume), nameof(PlayerViewModel.Repeat) })
            OnPlayerChanged(this, new PropertyChangedEventArgs(name));

        ApplySavedBounds(_vm.Store.Data);

        Overlay.Loaded += (_, _) =>
        {
            // Keys pressed while the video overlay window has focus should still work.
            if (GetWindow(Overlay) is { } overlayWindow && overlayWindow != this)
                overlayWindow.PreviewKeyDown += OnKeyDown;
        };
        Loaded += (_, _) =>
        {
            UpdateColumns();
            if (_startupFiles.Length > 0) _vm.Play(_startupFiles);
        };
        Closing += OnClosing;
    }

    private double _scrollBeforeRefresh;
    private ContextMenu? _openMenu;
    private Slider? _scrubbing;
    private object? _pressedRow;

    private ScrollViewer? ListScroller =>
        VisualTreeHelper.GetChildrenCount(PanelList) > 0 && VisualTreeHelper.GetChild(PanelList, 0) is Border { Child: ScrollViewer sv } ? sv : null;

    // ---- Window size and position ----

    private void ApplySavedBounds(Services.LibraryData data)
    {
        if (data.WindowWidth < MinWidth || data.WindowHeight < MinHeight) return;
        // Only restore a position that is still on a connected screen.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(data.WindowLeft, data.WindowTop, data.WindowWidth, data.WindowHeight);
        if (!screen.IntersectsWith(new Rect(bounds.Left, bounds.Top, bounds.Width, 40))) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (data.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveBounds(Services.LibraryData data)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty) return;
        data.WindowLeft = bounds.Left;
        data.WindowTop = bounds.Top;
        data.WindowWidth = bounds.Width;
        data.WindowHeight = bounds.Height;
        data.WindowMaximized = WindowState == WindowState.Maximized && !_vm.IsFullscreen
            || _vm.IsFullscreen && _stateBeforeFullscreen == WindowState.Maximized;
    }

    // ---- Layout ----

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsPanelOpen) or nameof(MainViewModel.IsPanelWide) or nameof(MainViewModel.IsFullscreen))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        var fullscreen = _vm.IsFullscreen;
        var open = _vm.IsPanelOpen && !fullscreen;

        TitleRow.Height = new GridLength(fullscreen ? 0 : 36);
        TitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        RailCol.Width = new GridLength(fullscreen ? 0 : RailWidth);
        RailPanel.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        LibraryPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        var width = _vm.Store.Data.PanelWidth;
        if (_vm.IsPanelWide) width = Math.Max(width * 2, (ActualWidth - RailWidth) * 0.55);
        PanelCol.MinWidth = open ? MinPanelWidth : 0;
        PanelCol.Width = new GridLength(open ? Math.Clamp(width, MinPanelWidth, Math.Max(MinPanelWidth, ActualWidth - RailWidth - 240)) : 0);
        FullscreenIcon.Data = (Geometry)FindResource(fullscreen ? "I.ExitFullscreen" : "I.Fullscreen");
        FullscreenButton.ToolTip = fullscreen ? "Exit fullscreen (Esc)" : "Fullscreen (F)";
    }

    private void OnSplitterDragged(object sender, DragCompletedEventArgs e)
    {
        if (!_vm.IsPanelWide) _vm.Store.Data.PanelWidth = PanelCol.ActualWidth;
    }

    private void ToggleFullscreen()
    {
        if (!_vm.IsFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            _vm.IsFullscreen = true;
            // Re-maximizing makes Windows ask for the max size again, which now covers the whole monitor.
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _vm.IsFullscreen = false;
            WindowState = WindowState.Normal;
            if (_stateBeforeFullscreen == WindowState.Maximized) WindowState = WindowState.Maximized;
        }
        ShowControls();
    }

    // ---- Player state → visuals ----

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        var p = _vm.Player;
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.IsPlaying):
                PlayIcon.Data = (Geometry)FindResource(p.IsPlaying ? "I.Pause" : "I.Play");
                PlayButton.ToolTip = p.IsPlaying ? "Pause (Space)" : "Play (Space)";
                ShowControls();
                break;
            case nameof(PlayerViewModel.Title):
            case nameof(PlayerViewModel.HasMedia):
                WindowTitle.Text = p.HasMedia && p.Title.Length > 0 ? p.Title : "VLC Media Player";
                Title = p.HasMedia && p.Title.Length > 0 ? $"{p.Title} - VLC Media Player" : "VLC Media Player";
                break;
            case nameof(PlayerViewModel.Volume):
            case nameof(PlayerViewModel.IsMuted):
                VolumeIcon.Data = (Geometry)FindResource(p.IsMuted || p.Volume == 0 ? "I.Mute" : p.Volume < 50 ? "I.VolumeLow" : "I.Volume");
                MuteButton.ToolTip = p.IsMuted ? "Unmute (M)" : "Mute (M)";
                break;
            case nameof(PlayerViewModel.Repeat):
                RepeatButton.Foreground = p.Repeat == RepeatMode.Off ? (Brush)FindResource("Text2Brush") : (Brush)FindResource("AccentBrush");
                RepeatOne.Visibility = p.Repeat == RepeatMode.One ? Visibility.Visible : Visibility.Collapsed;
                RepeatButton.ToolTip = p.Repeat switch { RepeatMode.All => "Repeat: all", RepeatMode.One => "Repeat: one", _ => "Repeat: off" };
                break;
        }
    }

    // ---- Auto-hiding controls ----

    private void OnOverlayMouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        _hideControls.Stop();
        if (!_controlsVisible)
        {
            _controlsVisible = true;
            ControlsBar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            Overlay.Cursor = null;
        }
        _hideControls.Start();
    }

    private void HideControls()
    {
        _hideControls.Stop();
        var p = _vm.Player;
        if (!p.HasVideo || !p.IsPlaying || ControlsBar.IsMouseOver || _openMenu?.IsOpen == true || _scrubbing != null)
        {
            if (p.HasVideo && p.IsPlaying) _hideControls.Start(); // check again later
            return;
        }
        _controlsVisible = false;
        ControlsBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
        Overlay.Cursor = Cursors.None;
    }

    private void OnOverlayClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && IsInside(d, ControlsBar)) return;
        if (!_vm.Player.HasVideo) return;

        if (e.ClickCount == 2)
        {
            _singleClick.Stop();
            ToggleFullscreen();
        }
        else
        {
            _singleClick.Start();
        }
    }

    private void OnOverlayWheel(object sender, MouseWheelEventArgs e)
    {
        _vm.Player.ChangeVolume(e.Delta > 0 ? 5 : -5);
        ShowControls();
    }

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var cur = d; cur != null; cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur == ancestor) return true;
        return false;
    }

    // ---- Seeking ----

    private void OnScrubStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        _scrubbing = slider;
        if (slider == SeekBar) _vm.Player.IsSeeking = true;
        slider.CaptureMouse();
        ScrubTo(slider, e);
        e.Handled = true;
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing == sender && e.LeftButton == MouseButtonState.Pressed) ScrubTo(_scrubbing, e);
    }

    private void OnScrubEnd(object sender, MouseButtonEventArgs e)
    {
        if (_scrubbing != sender) return;
        ScrubTo(_scrubbing, e);
        _scrubbing.ReleaseMouseCapture(); // raises LostMouseCapture, which finishes the seek
        e.Handled = true;
    }

    private void OnScrubLost(object sender, MouseEventArgs e)
    {
        if (_scrubbing != sender) return;
        var slider = _scrubbing;
        _scrubbing = null;
        if (slider == SeekBar)
        {
            _vm.Player.Seek(slider.Value);
            _vm.Player.IsSeeking = false;
        }
    }

    private static void ScrubTo(Slider slider, MouseEventArgs e)
    {
        if (slider.Template.FindName("PART_Track", slider) is not Track track) return;
        slider.Value = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), slider.Minimum, slider.Maximum);
    }

    private void OnSeekMoved(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm.Player.IsSeeking) _vm.Player.TimeText = _vm.Player.FormatAt(e.NewValue);
    }

    // ---- Menus ----

    private void OnMainMenu(object sender, RoutedEventArgs e) =>
        MenuBuilder.Show(_vm.MainMenu(), (UIElement)sender);

    private void OnPageMenu(object sender, RoutedEventArgs e)
    {
        if (_vm.Panel.Page.Menu is { } menu) MenuBuilder.Show(menu(), (UIElement)sender);
    }

    private void OnTracksMenu(object sender, RoutedEventArgs e)
    {
        _hideControls.Stop();
        _openMenu = MenuBuilder.Show(_vm.TracksMenu(), (UIElement)sender, PlacementMode.Top);
        _openMenu.Closed += (_, _) => ShowControls();
    }

    /// <summary>On a narrow video area, drop the title and volume slider so the transport stays centred and nothing clips.</summary>
    private void OnControlsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var compact = width < 700;
        ControlsTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        VolumeBar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        // Compact: transport on the left, the rest on the right, instead of centring and overlapping.
        ControlsLeftCol.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        // Very narrow: shuffle and repeat stay reachable from the tracks/playback menu.
        var tiny = width < 470;
        ShuffleButton.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        RepeatButton.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Library list ----

    private void OnRowPress(object sender, MouseButtonEventArgs e) =>
        _pressedRow = (sender as ListBoxItem)?.DataContext;

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: PanelRow row } || !ReferenceEquals(row, _pressedRow)) return;
        _pressedRow = null;
        // Run after the click finishes: invoking may rebuild the list this item belongs to.
        Dispatcher.BeginInvoke(() => Invoke(row));
    }

    private static void Invoke(PanelRow row) => row.Activate();

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: ItemRow { Menu: { } menu } } item) return;
        item.IsSelected = true;
        MenuBuilder.Show(menu(), item, PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PanelList.SelectedItem is PanelRow row)
        {
            Invoke(row);
            e.Handled = true;
        }
    }

    private void OnSearchToggled(object sender, RoutedEventArgs e)
    {
        if (_vm.Panel.IsSearchOpen) Dispatcher.BeginInvoke(() => SearchBox.Focus(), DispatcherPriority.Input);
        else PanelList.Focus();
    }

    // ---- Keyboard ----

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var p = _vm.Player;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var typing = Keyboard.FocusedElement is TextBox;
        var inList = Keyboard.FocusedElement is ListBoxItem or ListBox;

        bool handled = true;
        switch (key)
        {
            case Key.O when ctrl && shift: _vm.OpenFolder(); break;
            case Key.O when ctrl: _vm.OpenFiles(); break;
            case Key.N when ctrl: _vm.OpenNetworkStream(); break;
            case Key.B when ctrl: _vm.IsPanelOpen = !_vm.IsPanelOpen; break;
            case Key.F when ctrl:
                _vm.IsPanelOpen = true;
                _vm.Panel.IsSearchOpen = true;
                Activate(); // focus may be in the video overlay window
                Dispatcher.BeginInvoke(() => SearchBox.Focus(), DispatcherPriority.Input);
                break;
            case Key.Left when alt: _vm.Panel.BackCommand.Execute(null); break;
            case Key.Right when alt: _vm.Panel.ForwardCommand.Execute(null); break;
            case Key.Back when !typing: _vm.Panel.BackCommand.Execute(null); break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.Escape when _vm.IsFullscreen: ToggleFullscreen(); break;
            case Key.Escape when _vm.Panel.IsSearchOpen: _vm.Panel.ToggleSearchCommand.Execute(null); break;
            case Key.MediaPlayPause: p.TogglePlay(); break;
            case Key.MediaNextTrack: p.Next(); break;
            case Key.MediaPreviousTrack: p.Previous(); break;
            case Key.MediaStop: p.Stop(); break;
            default: handled = false; break;
        }

        if (!handled && !typing && !ctrl && !alt)
        {
            handled = true;
            switch (key)
            {
                case Key.Space: p.TogglePlay(); break;
                case Key.Left: p.SeekBy(shift ? -3000 : -10000); break;
                case Key.Right: p.SeekBy(shift ? 3000 : 10000); break;
                case Key.Up when !inList: p.ChangeVolume(5); break;
                case Key.Down when !inList: p.ChangeVolume(-5); break;
                case Key.F: ToggleFullscreen(); break;
                case Key.M: p.IsMuted = !p.IsMuted; break;
                case Key.N: p.Next(); break;
                case Key.P: p.Previous(); break;
                case Key.S: p.Stop(); break;
                default: handled = false; break;
            }
            if (handled) ShowControls();
        }

        e.Handled = handled;
    }

    // ---- Drag and drop ----

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _vm.Enqueue(files);
            else _vm.Play(files);
        }
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            _vm.Play(new[] { uri.ToString() });
        }
        e.Handled = true;
    }

    // ---- Caption buttons ----

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _hideControls.Stop();
        SaveBounds(_vm.Store.Data);
        _vm.Dispose();
    }

    // ---- IUiServices ----

    public string? PickFolder(string title)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        var owner = new WindowInteropHelper(this).Handle;
        return dialog.ShowDialog(new Win32Owner(owner)) == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string[] PickFiles(string filter, bool multiple)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, Multiselect = multiple };
        return dialog.ShowDialog(this) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? Prompt(string title, string hint, string initial, string okText) =>
        InputDialog.Prompt(this, title, hint, initial, okText);

    public bool Confirm(string title, string message, string okText) =>
        InputDialog.Confirm(this, title, message, okText);

    private sealed class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public Win32Owner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    // ---- Maximize to the work area (fullscreen: the whole monitor) ----

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        // Windows 11: keep rounded corners and a dark frame despite the custom chrome. Ignored on older Windows.
        int round = 2 /* DWMWCP_ROUND */, dark = 1, border = 0x00303030;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var area = _vm.IsFullscreen ? info.rcMonitor : info.rcWork;
        mmi.ptMaxPosition.X = area.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = area.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = area.Right - area.Left;
        mmi.ptMaxSize.Y = area.Bottom - area.Top;
        Marshal.StructureToPtr(mmi, lParam, true);
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
