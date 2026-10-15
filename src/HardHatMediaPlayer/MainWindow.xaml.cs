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
using HardHatMediaPlayer.Controls;
using HardHatMediaPlayer.ViewModels;
using HardHatMediaPlayer.Views;

namespace HardHatMediaPlayer;

public partial class MainWindow : Window, IUiServices
{
    public const string AppName = "Hard Hat Media Player";
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
        // Wait the system double-click time (touch double-taps are slower than mouse ones) before treating a click as single.
        _singleClick = new DispatcherTimer(TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime + 20), DispatcherPriority.Normal, (_, _) =>
        {
            _singleClick!.Stop();
            _vm.Player.TogglePlay();
        }, Dispatcher);
        _singleClick.Stop();

        _vm.PropertyChanged += OnViewModelChanged;
        _vm.Player.PropertyChanged += OnPlayerChanged;
        _vm.FullscreenToggled += ToggleFullscreen;
        _vm.ShortcutsRequested += ToggleShortcuts;
        _vm.Player.Osd += ShowOsd;
        _vm.Player.PlayToggled += FlashPlayState;
        ShortcutList.ItemsSource = Shortcuts;
        Panel.SetZIndex(ShortcutsLayer, 10);
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
            {
                overlayWindow.PreviewKeyDown += OnKeyDown;
                MakeNonActivating(overlayWindow);
                // Belt and braces: if it is activated anyway, hand activation straight back to this window.
                overlayWindow.Activated += (_, _) => Dispatcher.BeginInvoke(Activate);
            }
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
    private Point _pressPoint;

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
        // In fullscreen the window covers the monitor; remember the size it had before.
        var bounds = _vm.IsFullscreen ? _boundsBeforeFullscreen
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
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
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!_vm.IsFullscreen)
        {
            if (!GetMonitorRect(hwnd, out var monitor)) return;
            _stateBeforeFullscreen = WindowState;
            _boundsBeforeFullscreen = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _vm.IsFullscreen = true;

            // Windows keeps the taskbar above *maximized* windows even when they cover the screen.
            // A normal (un-maximized), frameless window sized exactly to the monitor is treated as
            // fullscreen and the taskbar steps aside.
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            SetFrameStyle(hwnd, fullscreen: true);
            SetWindowPos(hwnd, IntPtr.Zero, monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top,
                SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
            MarkFullscreen(true);
            Activate();
            Topmost = IsActive;
            _vm.Player.ShowOsd("Fullscreen", "Esc, F or double-click to exit", "Fullscreen");
        }
        else
        {
            _vm.IsFullscreen = false;
            Topmost = false;
            MarkFullscreen(false);
            ResizeMode = ResizeMode.CanResize;
            SetFrameStyle(hwnd, fullscreen: false);
            if (!_boundsBeforeFullscreen.IsEmpty)
            {
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
            }
            if (_stateBeforeFullscreen == WindowState.Maximized) WindowState = WindowState.Maximized;
        }
        ShowControls();
    }

    private Rect _boundsBeforeFullscreen = Rect.Empty;

    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>The full bounds (in physical pixels) of the monitor the window is on.</summary>
    private static bool GetMonitorRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        rect = info.rcMonitor;
        return true;
    }

    /// <summary>Tells the taskbar to get out of the way (or come back) for this window and the video overlay.</summary>
    private void MarkFullscreen(bool on)
    {
        try
        {
            _taskbar ??= CreateTaskbarList();
            if (_taskbar is null) return;
            _taskbar.MarkFullscreenWindow(new WindowInteropHelper(this).Handle, on);
            if (GetWindow(Overlay) is { } overlay && overlay != this)
                _taskbar.MarkFullscreenWindow(new WindowInteropHelper(overlay).Handle, on);
        }
        catch (COMException) { }
    }

    private ITaskbarList2? _taskbar;

    private static ITaskbarList2? CreateTaskbarList()
    {
        var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"));
        if (type is null || Activator.CreateInstance(type) is not ITaskbarList2 list) return null;
        list.HrInit();
        return list;
    }

    [ComImport, Guid("602D4995-B13A-429b-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
    }

    // ---- Player state → visuals ----

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        var p = _vm.Player;
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Position):
                SyncSeekBar();
                break;
            case nameof(PlayerViewModel.IsPlaying):
                PlayIcon.Data = (Geometry)FindResource(p.IsPlaying ? "I.Pause" : "I.Play");
                PlayButton.ToolTip = p.IsPlaying ? "Pause (Space)" : "Play (Space)";
                ShowControls();
                break;
            case nameof(PlayerViewModel.Title):
            case nameof(PlayerViewModel.HasMedia):
                WindowTitle.Text = p.HasMedia && p.Title.Length > 0 ? p.Title : AppName;
                Title = p.HasMedia && p.Title.Length > 0 ? $"{p.Title} - {AppName}" : AppName;
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
        if (!p.HasVideo || !p.IsPlaying || ControlsBar.IsMouseOver || QueuePanel.IsMouseOver || _openMenu?.IsOpen == true || _scrubbing != null)
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
        if (e.OriginalSource is DependencyObject d && (IsInside(d, ControlsBar) || IsInside(d, QueuePanel))) return;
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
        if (e.OriginalSource is DependencyObject d && IsInside(d, QueuePanel)) return; // scrolling the queue
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

    // The seek bar isn't data-bound: the player position is pushed into it (SyncSeekBar), and any change
    // that doesn't come from that push is the user's — a click, a drag, the keyboard or an accessibility tool.
    private bool _syncingSeekBar;

    private void SyncSeekBar()
    {
        if (_scrubbing == SeekBar) return;
        _syncingSeekBar = true;
        SeekBar.Value = _vm.Player.Position;
        _syncingSeekBar = false;
    }

    private void OnScrubStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider) return;
        _scrubbing = slider;
        if (slider == SeekBar) _vm.Player.IsSeeking = true;
        slider.CaptureMouse(); // best effort: the release below commits even without capture
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
        FinishScrub();
        e.Handled = true;
    }

    /// <summary>Capture can also end without a button-up (Alt+Tab, a tap that never captured); commit then too.</summary>
    private void OnScrubLost(object sender, MouseEventArgs e)
    {
        if (_scrubbing == sender) FinishScrub();
    }

    private void FinishScrub()
    {
        var slider = _scrubbing;
        if (slider is null) return;
        _scrubbing = null; // before releasing capture, so LostMouseCapture doesn't finish twice
        slider.ReleaseMouseCapture();
        if (slider == SeekBar)
        {
            _vm.Player.IsSeeking = false;
            _vm.Player.Seek(slider.Value);
        }
    }

    private static void ScrubTo(Slider slider, MouseEventArgs e)
    {
        if (slider.Template.FindName("PART_Track", slider) is not Track track) return;
        slider.Value = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), slider.Minimum, slider.Maximum);
    }

    private void OnSeekMoved(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingSeekBar) return;
        if (_scrubbing == SeekBar) _vm.Player.TimeText = _vm.Player.FormatAt(e.NewValue); // preview while dragging
        else _vm.Player.Seek(e.NewValue); // keyboard / accessibility change
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

    private void OnRowPress(object sender, MouseButtonEventArgs e)
    {
        _pressedRow = (sender as ListBoxItem)?.DataContext;
        _pressPoint = e.GetPosition((IInputElement)sender);
    }

    /// <summary>Dragging a library row carries its media, to drop on the queue (append) or the video (play).</summary>
    private void OnRowMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBoxItem { DataContext: ItemRow { DragPaths: { } paths } row } item
            || !ReferenceEquals(row, _pressedRow) || !IsDragGesture(e.GetPosition(item))) return;
        _pressedRow = null; // a drag isn't a click
        var files = paths().ToArray();
        if (files.Length > 0) DragDrop.DoDragDrop(item, new DataObject(PathsFormat, files), DragDropEffects.Copy);
    }

    private bool IsDragGesture(Point p) =>
        Math.Abs(p.X - _pressPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(p.Y - _pressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;

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
            case Key.Escape when ShortcutsLayer.IsVisible: ToggleShortcuts(); break;
            case Key.F1: ToggleShortcuts(); break;
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
                case Key.OemQuestion when shift: ToggleShortcuts(); break; // "?"
                case Key.OemOpenBrackets: p.Slower(); break;               // "["
                case Key.OemCloseBrackets: p.Faster(); break;              // "]"
                case Key.OemPlus when !shift: p.NormalSpeed(); break;      // "="
                case Key.V: p.CycleSubtitles(); break;
                case Key.B: p.CycleAudioTracks(); break;
                case Key.Q: _vm.IsQueueOpen = !_vm.IsQueueOpen; break;
                default: handled = false; break;
            }
        }

        e.Handled = handled;
    }

    // ---- Drag and drop ----

    /// <summary>Library rows dragged inside the app; holds their paths (files, folders or stream URLs).</summary>
    private const string PathsFormat = "HardHatMediaPlayer.Paths";

    /// <summary>A queue row being reordered; holds its index in the queue.</summary>
    private const string QueueIndexFormat = "HardHatMediaPlayer.QueueIndex";

    /// <summary>Library rows only drop onto the video or the queue; dropping one back on the library does nothing.</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PathsFormat)) return;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(PathsFormat) is string[] dragged)
        {
            if (sender != Overlay) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _vm.Enqueue(dragged);
            else _vm.Play(dragged);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
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

    // ---- Queue panel ----

    private QueueItem? _pressedQueueItem;

    private void OnOverlaySizeChanged(object sender, SizeChangedEventArgs e) =>
        QueuePanel.Width = Math.Clamp(e.NewSize.Width * 0.4, 220, 340);

    private void OnQueueRowPress(object sender, MouseButtonEventArgs e)
    {
        _pressedQueueItem = (sender as ListBoxItem)?.DataContext as QueueItem;
        _pressPoint = e.GetPosition((IInputElement)sender);
    }

    private void OnQueueRowMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBoxItem { DataContext: QueueItem queued } item
            || !ReferenceEquals(queued, _pressedQueueItem) || !IsDragGesture(e.GetPosition(item))) return;
        _pressedQueueItem = null;
        var index = _vm.Player.Queue.IndexOf(queued);
        if (index < 0) return;
        DragDrop.DoDragDrop(item, new DataObject(QueueIndexFormat, index), DragDropEffects.Move);
        HideQueueDropHints();
    }

    private void OnQueueRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: QueueItem queued } || !ReferenceEquals(queued, _pressedQueueItem)) return;
        _pressedQueueItem = null;
        Dispatcher.BeginInvoke(() => _vm.Player.PlayAt(queued));
    }

    private void OnQueueRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: QueueItem queued } item) return;
        _hideControls.Stop();
        _openMenu = MenuBuilder.Show(_vm.QueueItemMenu(queued), item, PlacementMode.MousePoint);
        _openMenu.Closed += (_, _) => ShowControls();
        e.Handled = true;
    }

    private void OnQueueDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(QueueIndexFormat))
        {
            e.Effects = DragDropEffects.Move;
            var (_, y) = QueueDropSlot(e);
            QueueDropLine.Width = Math.Max(0, QueueList.ActualWidth - 12);
            Canvas.SetLeft(QueueDropLine, 6);
            Canvas.SetTop(QueueDropLine, Math.Max(0, y - 1));
            QueueDropLine.Visibility = Visibility.Visible;
            ScrollQueueNearEdge(e.GetPosition(QueueList).Y);
        }
        else if (e.Data.GetDataPresent(PathsFormat) || e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            e.Effects = DragDropEffects.Copy;
            QueueDropHint.Visibility = Visibility.Visible;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnQueueDragLeave(object sender, DragEventArgs e) => HideQueueDropHints();

    private void HideQueueDropHints()
    {
        QueueDropLine.Visibility = Visibility.Collapsed;
        QueueDropHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>Reorders a dragged queue row, or appends whatever else was dropped on the queue.</summary>
    private void OnQueueDrop(object sender, DragEventArgs e)
    {
        HideQueueDropHints();
        e.Handled = true;
        if (e.Data.GetData(QueueIndexFormat) is int from)
        {
            var (slot, _) = QueueDropSlot(e);
            _vm.Player.MoveInQueue(from, slot > from ? slot - 1 : slot);
        }
        else if (e.Data.GetData(PathsFormat) is string[] dragged) _vm.Enqueue(dragged);
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) _vm.Enqueue(files);
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && !uri.IsFile)
            _vm.Enqueue(new[] { uri.ToString() });
    }

    /// <summary>The queue index a row dropped at the pointer would be inserted before, and the y of that gap in the list.</summary>
    private (int Slot, double Y) QueueDropSlot(DragEventArgs e)
    {
        var y = e.GetPosition(QueueList).Y;
        var generator = QueueList.ItemContainerGenerator;
        int last = -1;
        double bottom = 0;
        for (var i = 0; i < QueueList.Items.Count; i++)
        {
            if (generator.ContainerFromIndex(i) is not ListBoxItem row || !row.IsVisible) continue;
            var top = row.TranslatePoint(new Point(0, 0), QueueList).Y;
            if (y < top + row.ActualHeight / 2) return (i, top);
            last = i;
            bottom = top + row.ActualHeight;
        }
        return (last + 1, bottom);
    }

    private void ScrollQueueNearEdge(double y)
    {
        if (VisualTreeHelper.GetChildrenCount(QueueList) == 0 || VisualTreeHelper.GetChild(QueueList, 0) is not Border { Child: ScrollViewer sv }) return;
        if (y < 28) sv.ScrollToVerticalOffset(sv.VerticalOffset - 12);
        else if (y > QueueList.ActualHeight - 28) sv.ScrollToVerticalOffset(sv.VerticalOffset + 12);
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

    // ---- On-screen display ----

    private DispatcherTimer? _osdTimer;

    private void ShowOsd(OsdMessage m)
    {
        OsdText.Text = m.Text;
        OsdHint.Text = m.Hint ?? "";
        OsdHint.Visibility = m.Hint is null ? Visibility.Collapsed : Visibility.Visible;
        OsdIcon.Data = m.Icon is null ? null : TryFindResource("I." + m.Icon) as Geometry;
        OsdIcon.Visibility = OsdIcon.Data is null ? Visibility.Collapsed : Visibility.Visible;
        OsdLevel.Visibility = m.Level is null ? Visibility.Collapsed : Visibility.Visible;
        OsdLevelFill.Width = OsdLevel.Width * Math.Clamp(m.Level ?? 0, 0, 1);

        OsdPill.BeginAnimation(OpacityProperty, null);
        OsdPill.Opacity = 1;
        _osdTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1.6), DispatcherPriority.Normal, (_, _) =>
        {
            _osdTimer!.Stop();
            OsdPill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)));
        }, Dispatcher);
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private void FlashPlayState(bool playing)
    {
        if (!_vm.Player.HasVideo) return; // audio has its own card; a flash over it adds nothing
        CenterFlashIcon.Data = (Geometry)FindResource(playing ? "I.Play" : "I.Pause");
        var grow = new System.Windows.Media.Animation.DoubleAnimation(0.85, 1.15, TimeSpan.FromMilliseconds(500));
        CenterFlash.RenderTransformOrigin = new Point(0.5, 0.5);
        CenterFlash.RenderTransform = new ScaleTransform();
        ((ScaleTransform)CenterFlash.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ((ScaleTransform)CenterFlash.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        CenterFlash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)));
    }

    // ---- Keyboard shortcuts sheet ----

    private sealed record Shortcut(string Keys, string Action);

    private static readonly Shortcut[] Shortcuts =
    {
        new("Space", "Play / pause"),
        new("\u2190  \u2192", "Jump 10 s back / forward"),
        new("Shift + \u2190  \u2192", "Jump 3 s"),
        new("\u2191  \u2193  /  wheel", "Volume up / down"),
        new("M", "Mute"),
        new("F  /  F11", "Fullscreen"),
        new("Double-click", "Fullscreen"),
        new("Esc", "Leave fullscreen / close"),
        new("N  /  P", "Next / previous"),
        new("S", "Stop"),
        new("[  /  ]", "Slower / faster"),
        new("=", "Normal speed"),
        new("V", "Next subtitle track"),
        new("B", "Next audio track"),
        new("Ctrl + O", "Open file"),
        new("Ctrl + Shift + O", "Open folder"),
        new("Ctrl + N", "Open network stream"),
        new("Ctrl + B", "Show / hide library"),
        new("Q", "Show / hide the queue"),
        new("Ctrl + F", "Search the library page"),
        new("Alt + \u2190  \u2192", "Library back / forward"),
        new("Backspace", "Library back"),
        new("F1  /  ?", "This list"),
    };

    private void ToggleShortcuts()
    {
        ShortcutsLayer.Visibility = ShortcutsLayer.IsVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnShortcutsBackdrop(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == ShortcutsLayer) ToggleShortcuts();
        e.Handled = true; // don't let the click reach the video (pause / fullscreen)
    }

    // ---- Fullscreen helpers ----

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_vm.IsFullscreen) Topmost = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Only cover the taskbar while the video is the window in use; Alt+Tab must still work normally.
        if (Topmost && !IsOverlayActive()) Topmost = false;
    }

    private bool IsOverlayActive() => GetWindow(Overlay) is { } w && w != this && w.IsActive;

    private static void MakeNonActivating(Window window)
    {
        const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

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

    public void ShowSubtitleSearch(string videoPath)
    {
        // Fullscreen hides the title bar the dialog would centre under; leave fullscreen first.
        if (_vm.IsFullscreen) ToggleFullscreen();
        new SubtitleSearchWindow(this, _vm.Subtitles, _vm.Player, _vm.Store, videoPath).ShowDialog();
    }

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

        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        SetFrameStyle(hwnd, fullscreen: false);
    }

    /// <summary>
    /// Windows 11 frame: rounded corners and a subtle border in a window, square and borderless in fullscreen
    /// (otherwise the corners and a grey outline show around the video). Ignored on older Windows.
    /// </summary>
    private static void SetFrameStyle(IntPtr hwnd, bool fullscreen)
    {
        int corners = fullscreen ? 1 /* DWMWCP_DONOTROUND */ : 2 /* DWMWCP_ROUND */;
        int border = fullscreen ? unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */ : 0x00303030;
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corners, sizeof(int));
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
