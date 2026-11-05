using System.ComponentModel;
using System.Globalization;

namespace GeminiLiveSubtitles.Localization;

public static class I18n
{
    private static string _lang = "auto";
    private static bool _isZh = true;

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Status
        ["Status_Stopped"] = ("已暂停（右键或按 Alt+S 开始翻译）", "Paused (Right-click or press Alt+S to start)"),
        ["Status_Connecting"] = ("正在连接 Gemini…", "Connecting to Gemini..."),
        ["Status_Reconnecting"] = ("正在重新连接…", "Reconnecting to Gemini..."),
        ["Status_Connected"] = ("正在监听系统声音 → {0}（右键可调选项）", "Listening to system audio → {0} (Right-click for options)"),
        ["Status_Error"] = ("连接错误：{0}（自动重试中）", "Connection error: {0} (retrying...)"),
        ["Status_Cleared"] = ("已清空字幕历史", "Subtitle history cleared"),
        ["Status_Locked"] = ("已开启鼠标穿透（按 {0} 或托盘菜单恢复交互）", "Click-through enabled (press {0} or tray menu to restore)"),
        ["Status_Unlocked"] = ("已恢复字幕框交互（可直接拖动、缩放、右键）", "Interactive mode restored (drag, resize or right-click)"),
        ["Status_NeedKey"] = ("请先在设置中填写 API Key（右键字幕框可打开菜单）", "Please configure your Gemini API Key in Settings (Right-click for menu)"),
        ["Empty_Hint"] = ("正在监听系统声音…", "Listening to system audio..."),

        // Toolbar buttons
        ["Btn_Start"] = ("▶ 开始", "▶ Start"),
        ["Btn_Pause"] = ("⏸ 暂停", "⏸ Pause"),
        ["Btn_OrderTransFirst"] = ("译↑原↓", "Trans↑Orig↓"),
        ["Btn_OrderOrigFirst"] = ("原↑译↓", "Orig↑Trans↓"),
        ["Btn_AlignLeft"] = ("靠左", "Left"),
        ["Btn_AlignCenter"] = ("居中", "Center"),
        ["Btn_Clear"] = ("清空", "Clear"),
        ["Btn_PassThrough"] = ("穿透", "Click-thru"),
        ["Btn_Settings"] = ("⚙ 设置", "⚙ Settings"),
        ["Tip_StartPause"] = ("开始 / 暂停翻译 ({0})", "Start / Pause translation ({0})"),
        ["Tip_Order"] = ("切换译文在上 / 原文在上", "Swap translation / original order"),
        ["Tip_Align"] = ("切换字幕靠左 / 居中显示", "Toggle left / center alignment"),
        ["Tip_Clear"] = ("清空历史字幕 ({0})", "Clear subtitle history ({0})"),
        ["Tip_PassThrough"] = ("开启鼠标穿透（按 {0} 或托盘菜单可恢复交互）", "Enable click-through mode (press {0} or use tray to restore)"),
        ["Tip_Settings"] = ("打开设置", "Open settings"),

        // Context / Tray menu
        ["Menu_Start"] = ("开始翻译", "Start Translation"),
        ["Menu_Pause"] = ("暂停翻译", "Pause Translation"),
        ["Menu_TargetLang"] = ("目标语言", "Target Language"),
        ["Menu_TargetLangNamed"] = ("目标语言：{0}", "Target Language: {0}"),
        ["Menu_Content"] = ("字幕内容", "Subtitle Content"),
        ["Menu_Bilingual"] = ("双语（原文 + 译文）", "Bilingual (Original + Translation)"),
        ["Menu_TransOnly"] = ("仅显示译文", "Translation Only"),
        ["Menu_OrigOnly"] = ("仅显示原文", "Original Only"),
        ["Menu_Order"] = ("上下顺序", "Order"),
        ["Menu_OrderTransFirst"] = ("译文在上 · 原文在下", "Translation Top · Original Bottom"),
        ["Menu_OrderOrigFirst"] = ("原文在上 · 译文在下", "Original Top · Translation Bottom"),
        ["Menu_Align"] = ("文字对齐", "Alignment"),
        ["Menu_AlignLeft"] = ("靠左对齐", "Align Left"),
        ["Menu_AlignCenter"] = ("居中对齐", "Align Center"),
        ["Menu_CopyHistory"] = ("复制全部字幕历史", "Copy Subtitle History"),
        ["Menu_ClearHistory"] = ("清空字幕历史", "Clear History"),
        ["Menu_ClickThrough"] = ("鼠标穿透模式", "Click-Through Mode"),
        ["Menu_HideWindow"] = ("隐藏字幕窗口", "Hide Subtitles"),
        ["Menu_ShowWindow"] = ("显示字幕窗口", "Show Subtitles"),
        ["Menu_Settings"] = ("设置…", "Settings..."),
        ["Menu_Exit"] = ("退出", "Exit"),

        // Settings window
        ["Settings_Title"] = ("GeminiLiveSubtitles 设置", "GeminiLiveSubtitles Settings"),
        ["Settings_Sec_Trans"] = ("翻译与字幕排版", "Translation & Layout"),
        ["Settings_UiLang"] = ("界面语言", "UI Language"),
        ["Settings_TargetLang"] = ("目标语言", "Target Language"),
        ["Settings_TargetLang_Tip"] = ("可从列表选择，或直接输入 BCP-47 语言代码（如 pl、nl、tr）", "Select from list or type BCP-47 language code (e.g. pl, nl, tr)"),
        ["Settings_DisplayMode"] = ("字幕内容", "Subtitle Content"),
        ["Settings_Order"] = ("上下顺序", "Order"),
        ["Settings_Align"] = ("文字对齐", "Alignment"),
        ["Settings_HistoryMax"] = ("历史保留条数", "Max History Lines"),
        ["Settings_Sec_Appearance"] = ("字幕外观样式（实时预览 · 可同时拖动字幕框）", "Subtitle Appearance (Live Preview · Overlay remains interactive)"),
        ["Settings_Font"] = ("字体", "Font Family"),
        ["Settings_TransFontSize"] = ("译文字号", "Translation Font Size"),
        ["Settings_OrigFontSize"] = ("原文字号", "Original Font Size"),
        ["Settings_TransColor"] = ("译文颜色", "Translation Color"),
        ["Settings_OrigColor"] = ("原文颜色", "Original Color"),
        ["Settings_BgColor"] = ("背景颜色", "Background Color"),
        ["Settings_BgOpacity"] = ("背景不透明度", "Background Opacity"),
        ["Settings_HistoryOpacity"] = ("历史句不透明度", "History Opacity"),
        ["Settings_HistoryOpacity_Tip"] = ("上方已说完的历史句子的明暗度", "Brightness of completed history lines above the active line"),
        ["Settings_TextShadow"] = ("启用文字阴影描边（提升复杂背景下的清晰度）", "Enable text drop shadow (enhances legibility on complex backgrounds)"),
        ["Settings_VrCaptureMode"] = ("VR 捕获模式（任务栏显示 & 纯色背景，兼容 SteamVR 挂载）", "VR Capture Mode (Taskbar visible & solid background for SteamVR)"),
        ["Settings_Sec_Hotkeys"] = ("全局快捷键（点击输入框直接按下组合键，按 Backspace 清除）", "Global Hotkeys (Press hotkey combination directly in box, Backspace to clear)"),
        ["Settings_Hotkey_Run"] = ("开始 / 暂停", "Start / Pause"),
        ["Settings_Hotkey_Visible"] = ("显示 / 隐藏", "Show / Hide"),
        ["Settings_Hotkey_Lock"] = ("鼠标穿透切换", "Toggle Click-thru"),
        ["Settings_Hotkey_Clear"] = ("清空字幕历史", "Clear History"),
        ["Settings_Sec_Api"] = ("Gemini API 与网络", "Gemini API & Network"),
        ["Settings_ApiKey"] = ("API Key", "API Key"),
        ["Settings_Model"] = ("模型", "Model"),
        ["Settings_ApiVersion"] = ("API 版本", "API Version"),
        ["Settings_Proxy"] = ("代理 (可选)", "Proxy (Optional)"),
        ["Settings_Proxy_Tip"] = ("留空自动探测；可填 http://127.0.0.1:7897、socks5://127.0.0.1:1080 或 none（强制直连）", "Empty = auto-detect; or http://127.0.0.1:7897, socks5://..., none (direct)"),
        ["Settings_Save"] = ("保存设置", "Save"),
        ["Settings_Cancel"] = ("取消", "Cancel"),
        ["Msg_KeyRequired"] = ("请填写 Gemini API Key。", "Please enter your Gemini API Key."),
        ["Msg_AlreadyRunning"] = ("GeminiLiveSubtitles 已在运行（见系统托盘）。", "GeminiLiveSubtitles is already running (check system tray)."),
    };

    public static string CurrentLanguage => _lang;
    public static bool IsChinese => _isZh;

    public static void SetLanguage(string lang)
    {
        _lang = string.IsNullOrWhiteSpace(lang) ? "auto" : lang.Trim().ToLowerInvariant();
        if (_lang == "auto")
        {
            var ui = CultureInfo.CurrentUICulture.Name;
            _isZh = ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            _isZh = _lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
        Loc.Instance.Refresh();
    }

    public static string Get(string key, params object[] args)
    {
        if (!Strings.TryGetValue(key, out var pair)) return key;
        var template = _isZh ? pair.Zh : pair.En;
        return args.Length == 0 ? template : string.Format(template, args);
    }
}

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public string this[string key] => I18n.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
