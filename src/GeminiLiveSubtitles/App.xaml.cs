using System.Windows;
using System.Windows.Threading;
using GeminiLiveSubtitles.Gemini;
using GeminiLiveSubtitles.Localization;
using GeminiLiveSubtitles.Services;
using GeminiLiveSubtitles.Settings;
using GeminiLiveSubtitles.Subtitles;
using GeminiLiveSubtitles.ViewModels;
using GeminiLiveSubtitles.Views;

namespace GeminiLiveSubtitles;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private SettingsService _settings = null!;
    private OverlayViewModel _overlayVm = null!;
    private OverlayWindow _overlay = null!;
    private TrayIconService _tray = null!;
    private GlobalHotkeyService? _hotkeys;
    private TranslationSession _session = null!;
    private readonly SubtitleAssembler _assembler = new();
    private DispatcherTimer _tickTimer = null!;
    private DispatcherTimer _statusHideTimer = null!;
    private string _status = "";
    private bool _overlayVisible = true;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--smoke")
        {
            int code = await SmokeRunner.RunAsync(e.Args);
            Shutdown(code);
            return;
        }

        _singleInstance = new Mutex(true, @"Local\GeminiLiveSubtitles.SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(I18n.Get("Msg_AlreadyRunning"), "GeminiLiveSubtitles");
            Shutdown();
            return;
        }

        _settings = new SettingsService();
        I18n.SetLanguage(_settings.Current.UiLanguage);
        _status = I18n.Get("Status_Stopped");
        Log.Initialize(_settings.Directory);
        Log.Info("Application starting");
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var s = _settings.Current;
        _assembler.MaxEntries = s.MaxHistoryEntries;
        // Start in interactive (unlocked) mode so the subtitle box is immediately draggable & right-clickable.
        _overlayVm = new OverlayViewModel(_assembler.Entries) { IsLocked = false };
        _overlayVm.ApplyStyle(s);

        _overlay = new OverlayWindow(_overlayVm);
        if (s.WindowLeft is { } l && s.WindowTop is { } t && s.WindowWidth is { } w && s.WindowHeight is { } h && IsOnScreen(l, t, w, h))
        {
            _overlay.Left = l; _overlay.Top = t; _overlay.Width = w; _overlay.Height = h;
        }
        else
        {
            _overlay.PlaceDefault();
        }
        _overlay.ToggleRunRequested += ToggleRun;
        _overlay.ToggleLockRequested += ToggleLock;
        _overlay.ToggleVisibleRequested += ToggleVisible;
        _overlay.LanguageRequested += ChangeLanguage;
        _overlay.DisplayModeRequested += ChangeDisplayMode;
        _overlay.SubtitleOrderRequested += ChangeSubtitleOrder;
        _overlay.SubtitleAlignmentRequested += ChangeSubtitleAlignment;
        _overlay.ClearHistoryRequested += ClearHistory;
        _overlay.SettingsRequested += OpenSettings;
        _overlay.PlacementChanged += SaveWindowPlacement;
        _overlay.ExitRequested += () => Shutdown();
        _overlay.Show();

        _hotkeys = new GlobalHotkeyService(_overlay.Handle);
        _hotkeys.ToggleRunRequested += ToggleRun;
        _hotkeys.ToggleVisibleRequested += ToggleVisible;
        _hotkeys.ToggleClickThroughRequested += ToggleLock;
        _hotkeys.ClearHistoryRequested += ClearHistory;
        _hotkeys.Apply(s);

        _assembler.Changed += () =>
        {
            _overlayVm.OriginalText = _assembler.Original.DisplayText;
            _overlayVm.TranslatedText = _assembler.Translated.DisplayText;
            _overlay.ScrollToBottomIfSticky();
        };
        _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => _assembler.Tick(), Dispatcher);
        _tickTimer.Start();
        _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusHideTimer.Tick += (_, _) => { _statusHideTimer.Stop(); _overlayVm.IsStatusVisible = false; };

        _session = new TranslationSession();
        _session.InputTranscript += t => Dispatcher.BeginInvoke(() => _assembler.AppendOriginal(t));
        _session.OutputTranscript += t => Dispatcher.BeginInvoke(() => _assembler.AppendTranslated(t));
        _session.TurnComplete += () => Dispatcher.BeginInvoke(_assembler.TurnComplete);
        _session.StateChanged += (st, msg) => Dispatcher.BeginInvoke(() => OnSessionState(st, msg));

        _tray = new TrayIconService();
        _tray.ToggleRunRequested += ToggleRun;
        _tray.ToggleOrderRequested += () =>
            ChangeSubtitleOrder(_overlayVm.IsTranslationFirst ? SubtitleOrder.OriginalFirst : SubtitleOrder.TranslationFirst);
        _tray.ToggleAlignRequested += () =>
            ChangeSubtitleAlignment(_overlayVm.SubtitleAlignment == SubtitleAlignment.Left ? SubtitleAlignment.Center : SubtitleAlignment.Left);
        _tray.ClearHistoryRequested += ClearHistory;
        _tray.ToggleLockRequested += ToggleLock;
        _tray.ToggleVisibleRequested += ToggleVisible;
        _tray.LanguageRequested += ChangeLanguage;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += () => Shutdown();
        UpdateTray();

        if (_settings.GetApiKey() is null)
        {
            SetStatus(I18n.Get("Status_NeedKey"), sticky: true);
            OpenSettings();
        }
        else
        {
            StartSession();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_settings is not null && _overlay is not null)
            {
                SaveWindowPlacement();
                _hotkeys?.Dispose();
                _session?.Dispose();
                _tray?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error during shutdown", ex);
        }
        Log.Info("Application exiting");
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    // ---------------- Session ----------------

    private GeminiOptions? BuildOptions()
    {
        var key = _settings.GetApiKey();
        if (key is null) return null;
        var s = _settings.Current;
        return new GeminiOptions(key, s.Model, s.ApiVersion, s.TargetLanguage, s.ProxyUrl);
    }

    private void StartSession()
    {
        var opts = BuildOptions();
        if (opts is null)
        {
            OpenSettings();
            return;
        }
        _session.Start(opts);
        _overlayVm.IsRunning = true;
        UpdateTray();
    }

    private void StopSession()
    {
        _session.Stop();
        _overlayVm.IsRunning = false;
        SetStatus(I18n.Get("Status_Stopped"), sticky: false);
        UpdateTray();
    }

    private void RestartSessionIfRunning()
    {
        if (!_session.IsRunning) return;
        var opts = BuildOptions();
        if (opts is null) return;
        _session.Restart(opts);
        _overlayVm.IsRunning = true;
    }

    private void OnSessionState(ConnectionState state, string? message)
    {
        switch (state)
        {
            case ConnectionState.Connecting: SetStatus(I18n.Get("Status_Connecting"), sticky: true); break;
            case ConnectionState.Reconnecting: SetStatus(I18n.Get("Status_Reconnecting"), sticky: true); break;
            case ConnectionState.Connected:
                SetStatus(I18n.Get("Status_Connected", Languages.NameOf(_settings.Current.TargetLanguage)), sticky: false);
                break;
            case ConnectionState.Error:
                SetStatus(I18n.Get("Status_Error", Truncate(message, 80)), sticky: true);
                break;
            case ConnectionState.Stopped: break;
        }
        UpdateTray();
    }

    private void SetStatus(string text, bool sticky)
    {
        _status = text;
        _overlayVm.StatusText = text;
        _overlayVm.IsStatusVisible = true;
        _statusHideTimer.Stop();
        if (!sticky) _statusHideTimer.Start();
        UpdateTray();
    }

    // ---------------- Actions (context menu / hotkeys / tray) ----------------

    private void ToggleRun()
    {
        if (_session.IsRunning) StopSession();
        else StartSession();
    }

    private void ToggleLock()
    {
        _overlayVm.IsLocked = !_overlayVm.IsLocked;
        SaveWindowPlacement();
        if (!_overlayVisible) ToggleVisible();
        SetStatus(_overlayVm.IsLocked
            ? I18n.Get("Status_Locked", _settings.Current.HotkeyToggleClickThrough)
            : I18n.Get("Status_Unlocked"), sticky: false);
        UpdateTray();
    }

    private void ToggleVisible()
    {
        _overlayVisible = !_overlayVisible;
        if (_overlayVisible) _overlay.Show(); else _overlay.Hide();
        UpdateTray();
    }

    private void ClearHistory()
    {
        _assembler.Clear();
        SetStatus(I18n.Get("Status_Cleared"), sticky: false);
    }

    private void ChangeLanguage(string code)
    {
        var s = _settings.Current.Clone();
        if (string.Equals(s.TargetLanguage, code, StringComparison.OrdinalIgnoreCase)) return;
        s.TargetLanguage = code;
        _settings.Save(s);
        _overlayVm.ApplyStyle(s);
        RestartSessionIfRunning();
        UpdateTray();
    }

    private void ChangeDisplayMode(DisplayMode mode)
    {
        var s = _settings.Current.Clone();
        s.DisplayMode = mode;
        _settings.Save(s);
        _overlayVm.ApplyStyle(s);
        UpdateTray();
    }

    private void ChangeSubtitleOrder(SubtitleOrder order)
    {
        var s = _settings.Current.Clone();
        s.SubtitleOrder = order;
        _settings.Save(s);
        _overlayVm.ApplyStyle(s);
        UpdateTray();
    }

    private void ChangeSubtitleAlignment(SubtitleAlignment align)
    {
        var s = _settings.Current.Clone();
        s.SubtitleAlignment = align;
        _settings.Save(s);
        _overlayVm.ApplyStyle(s);
        UpdateTray();
    }

    /// <summary>
    /// Opens the settings window NON-MODALLY so the user can still freely drag, resize
    /// and interact with the subtitle overlay while tweaking settings.
    /// </summary>
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var before = _settings.Current.Clone();
        var oldKey = _settings.GetApiKey();
        var vm = new SettingsViewModel(before, oldKey);
        vm.PreviewRequested += preview => _overlayVm.ApplyStyle(preview);

        var win = new SettingsWindow(vm);
        _settingsWindow = win;
        win.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (!win.Saved)
            {
                // Restore current saved style (while keeping any placement changes made on the overlay).
                _overlayVm.ApplyStyle(_settings.Current);
                return;
            }

            var updated = vm.Build();
            // Preserve the overlay's live position/size in case the user dragged/resized it while Settings was open.
            updated.WindowLeft = _overlay.Left;
            updated.WindowTop = _overlay.Top;
            updated.WindowWidth = _overlay.Width;
            updated.WindowHeight = _overlay.Height;
            updated.Locked = _overlayVm.IsLocked;

            var newKey = vm.ApiKey.Trim();
            updated.ProtectedApiKey = newKey == oldKey ? before.ProtectedApiKey : SettingsService.Protect(newKey);
            _settings.Save(updated);
            I18n.SetLanguage(updated.UiLanguage);
            _assembler.MaxEntries = updated.MaxHistoryEntries;
            _overlayVm.ApplyStyle(updated);
            _hotkeys?.Apply(updated);

            bool connectionChanged = newKey != oldKey || updated.Model != before.Model ||
                                     updated.ApiVersion != before.ApiVersion || updated.ProxyUrl != before.ProxyUrl ||
                                     updated.TargetLanguage != before.TargetLanguage;
            if (!_session.IsRunning && oldKey is null) StartSession();
            else if (connectionChanged) RestartSessionIfRunning();
            UpdateTray();
        };
        win.Show();
    }

    // ---------------- Helpers ----------------

    private void UpdateTray() =>
        _tray?.Update(_session?.IsRunning ?? false, _overlayVm.IsLocked, _overlayVisible,
            _overlayVm.SubtitleOrder, _overlayVm.SubtitleAlignment, _settings.Current.TargetLanguage, _status);

    private void SaveWindowPlacement()
    {
        var s = _settings.Current.Clone();
        s.WindowLeft = _overlay.Left;
        s.WindowTop = _overlay.Top;
        s.WindowWidth = _overlay.Width;
        s.WindowHeight = _overlay.Height;
        s.Locked = _overlayVm.IsLocked;
        _settings.Save(s);
    }

    private static bool IsOnScreen(double left, double top, double width, double height) =>
        left + width > SystemParameters.VirtualScreenLeft + 50 &&
        top + height > SystemParameters.VirtualScreenTop + 20 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20;

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "未知错误" : s.Length <= max ? s : s[..max] + "…";
}
