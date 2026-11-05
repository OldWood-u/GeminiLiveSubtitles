namespace GeminiLiveSubtitles.Settings;

public enum DisplayMode
{
    Bilingual,
    TranslationOnly,
    OriginalOnly,
}

public enum SubtitleOrder
{
    TranslationFirst,
    OriginalFirst,
}

public enum SubtitleAlignment
{
    Left,
    Center,
}

/// <summary>Persisted user settings. Plain POCO so it round-trips through System.Text.Json.</summary>
public sealed class AppSettings
{
    // ---- API ----
    /// <summary>DPAPI-protected API key (base64). Never contains the plain key on disk.</summary>
    public string? ProtectedApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.5-live-translate-preview";
    public string ApiVersion { get; set; } = "v1beta";
    /// <summary>Optional proxy URL (e.g. http://127.0.0.1:7897 or socks5://127.0.0.1:1080). Empty = system default.</summary>
    public string? ProxyUrl { get; set; }

    // ---- Translation ----
    public string UiLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "zh-CN";

    // ---- Display ----
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Bilingual;
    public SubtitleOrder SubtitleOrder { get; set; } = SubtitleOrder.TranslationFirst;
    public SubtitleAlignment SubtitleAlignment { get; set; } = SubtitleAlignment.Left;
    public int MaxHistoryEntries { get; set; } = 200;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double TranslatedFontSize { get; set; } = 24;
    public double OriginalFontSize { get; set; } = 15;
    public string TranslatedColor { get; set; } = "#FFFFFFFF";
    public string OriginalColor { get; set; } = "#FFB8C0CC";
    public string BackgroundColor { get; set; } = "#FF101216";
    /// <summary>0..1</summary>
    public double BackgroundOpacity { get; set; } = 0.72;
    /// <summary>Opacity of completed history lines above the active line (0.1..1.0).</summary>
    public double HistoryOpacity { get; set; } = 0.50;
    public bool TextShadow { get; set; } = true;

    // ---- Window ----
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    /// <summary>Click-through mode. Off by default so the window can be dragged, resized and right-clicked directly.</summary>
    public bool Locked { get; set; } = false;
    /// <summary>VR capture mode with opaque dark solid background, taskbar presence, and fixed overlay window for SteamVR capture.</summary>
    public bool IsVrCaptureMode { get; set; } = false;

    // ---- Hotkeys (e.g. "Alt+S", empty to disable) ----
    public string HotkeyToggleRun { get; set; } = "Alt+S";
    public string HotkeyToggleVisible { get; set; } = "Alt+H";
    public string HotkeyToggleClickThrough { get; set; } = "Alt+T";
    public string HotkeyClearHistory { get; set; } = "Alt+C";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
