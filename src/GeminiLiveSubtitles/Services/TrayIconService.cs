using System.Drawing;
using System.IO;
using GeminiLiveSubtitles.Localization;
using GeminiLiveSubtitles.Settings;
using WinForms = System.Windows.Forms;

namespace GeminiLiveSubtitles.Services;

/// <summary>System tray icon + clean native context menu. All callbacks run on the UI thread.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _toggleRunItem;
    private readonly WinForms.ToolStripMenuItem _orderItem;
    private readonly WinForms.ToolStripMenuItem _alignItem;
    private readonly WinForms.ToolStripMenuItem _clearItem;
    private readonly WinForms.ToolStripMenuItem _lockItem;
    private readonly WinForms.ToolStripMenuItem _visibleItem;
    private readonly WinForms.ToolStripMenuItem _languageMenu;
    private readonly WinForms.ToolStripMenuItem _settingsItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;

    public event Action? ToggleRunRequested;
    public event Action? ToggleOrderRequested;
    public event Action? ToggleAlignRequested;
    public event Action? ClearHistoryRequested;
    public event Action? ToggleLockRequested;
    public event Action? ToggleVisibleRequested;
    public event Action<string>? LanguageRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _statusItem = new WinForms.ToolStripMenuItem(I18n.Get("Status_Stopped")) { Enabled = false };
        _toggleRunItem = new WinForms.ToolStripMenuItem($"{I18n.Get("Menu_Start")} (Alt+S)", null, (_, _) => ToggleRunRequested?.Invoke());
        _orderItem = new WinForms.ToolStripMenuItem(I18n.Get("Menu_OrderTransFirst"), null, (_, _) => ToggleOrderRequested?.Invoke());
        _alignItem = new WinForms.ToolStripMenuItem(I18n.Get("Menu_AlignLeft"), null, (_, _) => ToggleAlignRequested?.Invoke());
        _clearItem = new WinForms.ToolStripMenuItem($"{I18n.Get("Menu_ClearHistory")} (Alt+C)", null, (_, _) => ClearHistoryRequested?.Invoke());
        _lockItem = new WinForms.ToolStripMenuItem($"{I18n.Get("Menu_ClickThrough")} (Alt+T)", null, (_, _) => ToggleLockRequested?.Invoke());
        _visibleItem = new WinForms.ToolStripMenuItem($"{I18n.Get("Menu_ShowWindow")} (Alt+H)", null, (_, _) => ToggleVisibleRequested?.Invoke());
        _languageMenu = new WinForms.ToolStripMenuItem(I18n.Get("Menu_TargetLang"));
        foreach (var lang in Languages.All)
        {
            var code = lang.Code;
            _languageMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem(lang.ToString(), null, (_, _) => LanguageRequested?.Invoke(code))
            {
                Tag = code,
            });
        }

        _settingsItem = new WinForms.ToolStripMenuItem(I18n.Get("Menu_Settings"), null, (_, _) => SettingsRequested?.Invoke());
        _exitItem = new WinForms.ToolStripMenuItem(I18n.Get("Menu_Exit"), null, (_, _) => ExitRequested?.Invoke());

        _menu = new WinForms.ContextMenuStrip
        {
            RenderMode = WinForms.ToolStripRenderMode.System,
            ShowImageMargin = false,
            ShowCheckMargin = true,
        };
        _menu.Items.AddRange(
        [
            _statusItem,
            new WinForms.ToolStripSeparator(),
            _toggleRunItem,
            _languageMenu,
            _orderItem,
            _alignItem,
            _clearItem,
            new WinForms.ToolStripSeparator(),
            _visibleItem,
            _lockItem,
            new WinForms.ToolStripSeparator(),
            _settingsItem,
            _exitItem,
        ]);

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "GeminiLiveSubtitles",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ToggleVisibleRequested?.Invoke();
    }

    public void Update(bool running, bool locked, bool visible, SubtitleOrder order, SubtitleAlignment align, string targetLanguage, string status)
    {
        _toggleRunItem.Text = running ? $"{I18n.Get("Menu_Pause")} (Alt+S)" : $"{I18n.Get("Menu_Start")} (Alt+S)";
        _orderItem.Text = order == SubtitleOrder.TranslationFirst
            ? $"{I18n.Get("Menu_Order")}：{I18n.Get("Menu_OrderTransFirst")}"
            : $"{I18n.Get("Menu_Order")}：{I18n.Get("Menu_OrderOrigFirst")}";
        _alignItem.Text = align == SubtitleAlignment.Center
            ? $"{I18n.Get("Menu_Align")}：{I18n.Get("Menu_AlignCenter")}"
            : $"{I18n.Get("Menu_Align")}：{I18n.Get("Menu_AlignLeft")}";
        _lockItem.Text = $"{I18n.Get("Menu_ClickThrough")} (Alt+T)";
        _clearItem.Text = $"{I18n.Get("Menu_ClearHistory")} (Alt+C)";
        _visibleItem.Text = visible ? $"{I18n.Get("Menu_HideWindow")} (Alt+H)" : $"{I18n.Get("Menu_ShowWindow")} (Alt+H)";
        _settingsItem.Text = I18n.Get("Menu_Settings");
        _exitItem.Text = I18n.Get("Menu_Exit");
        _lockItem.Checked = locked;
        _visibleItem.Checked = visible;
        foreach (WinForms.ToolStripMenuItem item in _languageMenu.DropDownItems)
            item.Checked = string.Equals((string?)item.Tag, targetLanguage, StringComparison.OrdinalIgnoreCase);
        _languageMenu.Text = I18n.Get("Menu_TargetLangNamed", Languages.NameOf(targetLanguage));
        _statusItem.Text = status;
        var tip = $"GeminiLiveSubtitles - {status}";
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    public void ShowBalloon(string title, string text, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(2500, title, text, icon);

    private static Icon LoadIcon()
    {
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res is not null)
            {
                using var s = res.Stream;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                return new Icon(ms, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load tray icon: {ex.Message}");
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _menu.Dispose();
        _icon.Dispose();
    }
}
