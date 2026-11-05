using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using GeminiLiveSubtitles.Interop;
using GeminiLiveSubtitles.Localization;
using GeminiLiveSubtitles.Settings;
using GeminiLiveSubtitles.Subtitles;
using GeminiLiveSubtitles.ViewModels;

namespace GeminiLiveSubtitles.Views;

public partial class OverlayWindow : Window
{
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private readonly OverlayViewModel _vm;
    private nint _hwnd;
    private bool _autoScroll = true;
    private bool _smoothScrolling;
    private double _targetVerticalOffset;

    public OverlayWindow(OverlayViewModel vm)
    {
        _vm = vm;
        DataContext = vm;

        // Both standard overlay mode and VR capture mode use borderless transparent window style
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;

        if (vm.IsVrCaptureMode)
        {
            ShowInTaskbar = true;
            Title = "GeminiSubtitles - VR";
        }
        else
        {
            ShowInTaskbar = false;
            Title = "GeminiLiveSubtitles";
        }

        InitializeComponent();
        vm.PropertyChanged += OnVmPropertyChanged;
        Backdrop.ContextMenu = BuildContextMenu();
    }

    public nint Handle => _hwnd;

    public event Action? ToggleRunRequested;
    public event Action? ToggleLockRequested;
    public event Action? ToggleVisibleRequested;
    public event Action<string>? LanguageRequested;
    public event Action<DisplayMode>? DisplayModeRequested;
    public event Action<SubtitleOrder>? SubtitleOrderRequested;
    public event Action<SubtitleAlignment>? SubtitleAlignmentRequested;
    public event Action? ClearHistoryRequested;
    public event Action? SettingsRequested;
    public event Action? PlacementChanged;
    public event Action? ExitRequested;

    /// <summary>Opens the WPF Fluent context menu (also invoked when right-clicking the system tray icon).</summary>
    public void OpenContextMenuFromTray()
    {
        if (Backdrop.ContextMenu is not { } menu) return;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyVrCaptureMode(_vm.IsVrCaptureMode);
        ApplyLock(_vm.IsLocked);
    }

    /// <summary>Default placement: bottom-center of the primary work area, 60% width.</summary>
    public void PlaceDefault()
    {
        var wa = SystemParameters.WorkArea;
        Width = Math.Round(wa.Width * 0.6);
        Height = 200;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Bottom - Height - 36;
    }

    // ---- Smooth animated scrolling & entry animations ----

    public void ScrollToBottomIfSticky()
    {
        if (!_autoScroll) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, StartSmoothScrollToBottom);
    }

    private void StartSmoothScrollToBottom()
    {
        HistoryScroll.UpdateLayout();
        _targetVerticalOffset = HistoryScroll.ScrollableHeight;
        if (_targetVerticalOffset <= 0) return;

        if (!_smoothScrolling)
        {
            _smoothScrolling = true;
            CompositionTarget.Rendering += OnSmoothScrollFrame;
        }
    }

    private void OnSmoothScrollFrame(object? sender, EventArgs e)
    {
        if (!_autoScroll)
        {
            StopSmoothScroll();
            return;
        }
        _targetVerticalOffset = HistoryScroll.ScrollableHeight;
        double current = HistoryScroll.VerticalOffset;
        double diff = _targetVerticalOffset - current;
        if (Math.Abs(diff) < 0.6)
        {
            HistoryScroll.ScrollToVerticalOffset(_targetVerticalOffset);
            StopSmoothScroll();
            return;
        }
        HistoryScroll.ScrollToVerticalOffset(current + diff * 0.22);
    }

    private void StopSmoothScroll()
    {
        if (!_smoothScrolling) return;
        _smoothScrolling = false;
        CompositionTarget.Rendering -= OnSmoothScrollFrame;
    }

    private void HistoryScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            // Scrolling up to inspect history -> pause sticky auto-scroll.
            _autoScroll = false;
            StopSmoothScroll();
        }
    }

    private void HistoryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0 && !_smoothScrolling)
        {
            _autoScroll = HistoryScroll.VerticalOffset + HistoryScroll.ViewportHeight >= HistoryScroll.ExtentHeight - 20;
        }
        else if (e.ExtentHeightChange > 0 && _autoScroll)
        {
            StartSmoothScrollToBottom();
        }
    }

    /// <summary>Slide-up + fade-in animation when a new utterance line is created.</summary>
    private void ItemCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { RenderTransform: TranslateTransform tt } card) return;
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
        if (card.DataContext is SubtitleEntry { IsActive: true })
        {
            var anim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = EaseOut,
                FillBehavior = FillBehavior.Stop,
            };
            card.BeginAnimation(OpacityProperty, anim);
        }
    }

    /// <summary>Subtle micro-motion as new streaming words arrive on the active sentence.</summary>
    private void SubtitleText_TargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not SubtitleEntry { IsActive: true }) return;
        if (VisualTreeHelper.GetParent(tb) is not Grid g || VisualTreeHelper.GetParent(g) is not Border { RenderTransform: TranslateTransform tt }) return;

        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(2.2, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsLocked)) ApplyLock(_vm.IsLocked);
        else if (e.PropertyName == nameof(OverlayViewModel.IsVrCaptureMode)) ApplyVrCaptureMode(_vm.IsVrCaptureMode);
    }

    private void ApplyLock(bool locked)
    {
        if (_hwnd == 0) return;
        // In VR capture mode, never enable click-through/layered styles that break SteamVR capture
        if (_vm.IsVrCaptureMode) return;

        if (locked)
            NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED, 0);
        else
            NativeMethods.SetExStyle(_hwnd, 0, NativeMethods.WS_EX_TRANSPARENT);
    }

    private void ApplyVrCaptureMode(bool enabled)
    {
        Owner = null;
        ShowInTaskbar = enabled;
        Title = enabled ? "GeminiSubtitles - VR" : "GeminiLiveSubtitles";

        if (_hwnd == 0) return;

        if (enabled)
        {
            // VR capture mode: Must NOT have WS_EX_TOOLWINDOW, WS_EX_NOACTIVATE, or WS_EX_TRANSPARENT.
            // Only add WS_EX_APPWINDOW so SteamVR and Windows treat it as a standard capturable window.
            NativeMethods.SetExStyle(_hwnd,
                NativeMethods.WS_EX_APPWINDOW,
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT);
        }
        else
        {
            // Standard overlay mode: tool window hidden from Alt+Tab and taskbar.
            NativeMethods.SetExStyle(_hwnd,
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                NativeMethods.WS_EX_APPWINDOW);
        }
    }

    /// <summary>
    /// Tunnel handler so clicking ANYWHERE inside the subtitle box (even on text or inside the ScrollViewer)
    /// drags the window smoothly, unless clicking a button or a resize edge/corner thumb.
    /// </summary>
    private void Backdrop_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsLocked) return;
        if (IsInsideInteractiveControl(e.OriginalSource as DependencyObject)) return;
        try
        {
            DragMove();
            PlacementChanged?.Invoke();
        }
        catch (InvalidOperationException) { }
    }

    private static bool IsInsideInteractiveControl(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ButtonBase or Thumb or MenuItem) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_vm.IsLocked || sender is not Thumb { Tag: string dir }) return;

        if (dir.Contains('R'))
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        }
        if (dir.Contains('B'))
        {
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }
        if (dir.Contains('L'))
        {
            double newW = Math.Max(MinWidth, Width - e.HorizontalChange);
            Left += Width - newW;
            Width = newW;
        }
        if (dir.Contains('T'))
        {
            double newH = Math.Max(MinHeight, Height - e.VerticalChange);
            Top += Height - newH;
            Height = newH;
        }
        PlacementChanged?.Invoke();
    }

    // ---- Hover toolbar buttons ----
    private void ToggleRun_Click(object sender, RoutedEventArgs e) => ToggleRunRequested?.Invoke();
    private void ToggleOrder_Click(object sender, RoutedEventArgs e) =>
        SubtitleOrderRequested?.Invoke(_vm.IsTranslationFirst ? SubtitleOrder.OriginalFirst : SubtitleOrder.TranslationFirst);
    private void ToggleAlign_Click(object sender, RoutedEventArgs e) =>
        SubtitleAlignmentRequested?.Invoke(_vm.SubtitleAlignment == SubtitleAlignment.Left ? SubtitleAlignment.Center : SubtitleAlignment.Left);
    private void ClearHistory_Click(object sender, RoutedEventArgs e) => ClearHistoryRequested?.Invoke();
    private void Lock_Click(object sender, RoutedEventArgs e) => ToggleLockRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    // ---- Rich right-click context menu directly on the subtitle window ----
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var runItem = new MenuItem { InputGestureText = "Alt+S" };
        runItem.Click += (_, _) => ToggleRunRequested?.Invoke();

        // Target language submenu
        var langMenu = new MenuItem { Header = "目标语言" };
        foreach (var lang in Languages.All)
        {
            var code = lang.Code;
            var item = new MenuItem { Header = lang.ToString(), Tag = code, IsCheckable = true };
            item.Click += (_, _) => LanguageRequested?.Invoke(code);
            langMenu.Items.Add(item);
        }

        // Display mode submenu
        var modeMenu = new MenuItem { Header = "字幕内容" };
        AddModeItem(modeMenu, DisplayMode.Bilingual, "双语（原文 + 译文）");
        AddModeItem(modeMenu, DisplayMode.TranslationOnly, "仅显示译文");
        AddModeItem(modeMenu, DisplayMode.OriginalOnly, "仅显示原文");

        // Order submenu
        var orderMenu = new MenuItem { Header = "上下顺序" };
        var transFirst = new MenuItem { Header = "译文在上 · 原文在下", Tag = SubtitleOrder.TranslationFirst, IsCheckable = true };
        transFirst.Click += (_, _) => SubtitleOrderRequested?.Invoke(SubtitleOrder.TranslationFirst);
        var origFirst = new MenuItem { Header = "原文在上 · 译文在下", Tag = SubtitleOrder.OriginalFirst, IsCheckable = true };
        origFirst.Click += (_, _) => SubtitleOrderRequested?.Invoke(SubtitleOrder.OriginalFirst);
        orderMenu.Items.Add(transFirst);
        orderMenu.Items.Add(origFirst);

        // Alignment submenu
        var alignMenu = new MenuItem { Header = "文字对齐" };
        var alignLeft = new MenuItem { Header = "靠左对齐", Tag = SubtitleAlignment.Left, IsCheckable = true };
        alignLeft.Click += (_, _) => SubtitleAlignmentRequested?.Invoke(SubtitleAlignment.Left);
        var alignCenter = new MenuItem { Header = "居中对齐", Tag = SubtitleAlignment.Center, IsCheckable = true };
        alignCenter.Click += (_, _) => SubtitleAlignmentRequested?.Invoke(SubtitleAlignment.Center);
        alignMenu.Items.Add(alignLeft);
        alignMenu.Items.Add(alignCenter);

        var copyItem = new MenuItem { Header = "复制全部字幕历史" };
        copyItem.Click += (_, _) => CopyHistory();

        var clearItem = new MenuItem { Header = "清空字幕历史", InputGestureText = "Alt+C" };
        clearItem.Click += (_, _) => ClearHistoryRequested?.Invoke();

        var lockItem = new MenuItem { Header = "鼠标穿透模式", InputGestureText = "Alt+T" };
        lockItem.Click += (_, _) => ToggleLockRequested?.Invoke();

        var hideItem = new MenuItem { Header = "隐藏字幕窗口", InputGestureText = "Alt+H" };
        hideItem.Click += (_, _) => ToggleVisibleRequested?.Invoke();

        var settingsItem = new MenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(runItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(langMenu);
        menu.Items.Add(modeMenu);
        menu.Items.Add(orderMenu);
        menu.Items.Add(alignMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(lockItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) =>
        {
            runItem.Header = I18n.Get(_vm.IsRunning ? "Menu_Pause" : "Menu_Start");
            langMenu.Header = I18n.Get("Menu_TargetLang");
            modeMenu.Header = I18n.Get("Menu_Content");
            orderMenu.Header = I18n.Get("Menu_Order");
            transFirst.Header = I18n.Get("Menu_OrderTransFirst");
            origFirst.Header = I18n.Get("Menu_OrderOrigFirst");
            alignMenu.Header = I18n.Get("Menu_Align");
            alignLeft.Header = I18n.Get("Menu_AlignLeft");
            alignCenter.Header = I18n.Get("Menu_AlignCenter");
            copyItem.Header = I18n.Get("Menu_CopyHistory");
            clearItem.Header = I18n.Get("Menu_ClearHistory");
            lockItem.Header = _vm.IsLocked
                ? $"{I18n.Get("Menu_ClickThrough")}  ✓"
                : I18n.Get("Menu_ClickThrough");
            hideItem.Header = I18n.Get(IsVisible ? "Menu_HideWindow" : "Menu_ShowWindow");
            settingsItem.Header = I18n.Get("Menu_Settings");
            exitItem.Header = I18n.Get("Menu_Exit");

            foreach (MenuItem i in langMenu.Items)
                i.IsChecked = string.Equals((string?)i.Tag, _vm.TargetLanguage, StringComparison.OrdinalIgnoreCase);
            foreach (MenuItem i in modeMenu.Items)
            {
                var m = (DisplayMode)i.Tag!;
                i.Header = I18n.Get(m switch
                {
                    DisplayMode.TranslationOnly => "Menu_TransOnly",
                    DisplayMode.OriginalOnly => "Menu_OrigOnly",
                    _ => "Menu_Bilingual"
                });
                i.IsChecked = m == _vm.DisplayMode;
            }
            transFirst.IsChecked = _vm.SubtitleOrder == SubtitleOrder.TranslationFirst;
            origFirst.IsChecked = _vm.SubtitleOrder == SubtitleOrder.OriginalFirst;
            alignLeft.IsChecked = _vm.SubtitleAlignment == SubtitleAlignment.Left;
            alignCenter.IsChecked = _vm.SubtitleAlignment == SubtitleAlignment.Center;
            copyItem.IsEnabled = _vm.Entries.Count > 0;
            clearItem.IsEnabled = _vm.Entries.Count > 0;
        };
        return menu;
    }

    private void AddModeItem(MenuItem parent, DisplayMode mode, string header)
    {
        var item = new MenuItem { Header = header, Tag = mode, IsCheckable = true };
        item.Click += (_, _) => DisplayModeRequested?.Invoke(mode);
        parent.Items.Add(item);
    }

    private void CopyHistory()
    {
        if (_vm.Entries.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var e in _vm.Entries)
        {
            if (_vm.IsTranslationFirst)
            {
                if (!string.IsNullOrEmpty(e.Translated)) sb.AppendLine($"[{e.TimeText}] {e.Translated}");
                if (!string.IsNullOrEmpty(e.Original)) sb.AppendLine($"           {e.Original}");
            }
            else
            {
                if (!string.IsNullOrEmpty(e.Original)) sb.AppendLine($"[{e.TimeText}] {e.Original}");
                if (!string.IsNullOrEmpty(e.Translated)) sb.AppendLine($"           {e.Translated}");
            }
        }
        try { Clipboard.SetText(sb.ToString()); } catch { /* ignore */ }
    }
}
