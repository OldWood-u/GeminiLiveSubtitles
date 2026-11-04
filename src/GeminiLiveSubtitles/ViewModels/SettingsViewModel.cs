using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GeminiLiveSubtitles.Localization;
using GeminiLiveSubtitles.Settings;

namespace GeminiLiveSubtitles.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;

    public SettingsViewModel(AppSettings current, string? currentApiKey)
    {
        _original = current.Clone();
        _apiKey = currentApiKey ?? "";
        _model = current.Model;
        _apiVersion = current.ApiVersion;
        _proxyUrl = current.ProxyUrl ?? "";
        _uiLanguage = current.UiLanguage ?? "auto";
        _targetLanguage = Languages.All.FirstOrDefault(l => string.Equals(l.Code, current.TargetLanguage, StringComparison.OrdinalIgnoreCase))
            ?.ToString() ?? current.TargetLanguage;
        _displayMode = current.DisplayMode;
        _subtitleOrder = current.SubtitleOrder;
        _subtitleAlignment = current.SubtitleAlignment;
        _maxHistoryEntries = current.MaxHistoryEntries;
        _fontFamily = current.FontFamily;
        _translatedFontSize = current.TranslatedFontSize;
        _originalFontSize = current.OriginalFontSize;
        _translatedColor = current.TranslatedColor;
        _originalColor = current.OriginalColor;
        _backgroundColor = current.BackgroundColor;
        _backgroundOpacityPercent = Math.Round(current.BackgroundOpacity * 100);
        _historyOpacityPercent = Math.Round(current.HistoryOpacity * 100);
        _textShadow = current.TextShadow;
        _hotkeyToggleRun = current.HotkeyToggleRun;
        _hotkeyToggleVisible = current.HotkeyToggleVisible;
        _hotkeyToggleClickThrough = current.HotkeyToggleClickThrough;
        _hotkeyClearHistory = current.HotkeyClearHistory;
        PropertyChanged += OnAnyPropertyChanged;
    }

    public IReadOnlyList<KeyValuePair<string, string>> UiLanguages { get; } =
    [
        new("auto", "跟随系统 / Follow System"),
        new("zh-CN", "简体中文 (Chinese Simplified)"),
        new("en", "English"),
    ];

    public IReadOnlyList<LanguageOption> LanguageOptions => Languages.All;
    public IReadOnlyList<string> ApiVersions { get; } = ["v1beta", "v1alpha"];
    public IReadOnlyList<string> FontFamilies { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList();

    public IReadOnlyList<KeyValuePair<DisplayMode, string>> DisplayModes { get; } =
    [
        new(DisplayMode.Bilingual, "双语（原文 + 译文）"),
        new(DisplayMode.TranslationOnly, "仅译文"),
        new(DisplayMode.OriginalOnly, "仅原文"),
    ];

    public IReadOnlyList<KeyValuePair<SubtitleOrder, string>> SubtitleOrders { get; } =
    [
        new(SubtitleOrder.TranslationFirst, "译文在上 · 原文在下"),
        new(SubtitleOrder.OriginalFirst, "原文在上 · 译文在下"),
    ];

    public IReadOnlyList<KeyValuePair<SubtitleAlignment, string>> SubtitleAlignments { get; } =
    [
        new(SubtitleAlignment.Left, "靠左对齐"),
        new(SubtitleAlignment.Center, "居中对齐"),
    ];

    [ObservableProperty] private string _apiKey;
    [ObservableProperty] private string _model;
    [ObservableProperty] private string _apiVersion;
    [ObservableProperty] private string _proxyUrl;
    [ObservableProperty] private string _uiLanguage;
    partial void OnUiLanguageChanged(string value) => I18n.SetLanguage(value);
    [ObservableProperty] private string _targetLanguage;
    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private SubtitleOrder _subtitleOrder;
    [ObservableProperty] private SubtitleAlignment _subtitleAlignment;
    [ObservableProperty] private int _maxHistoryEntries;
    [ObservableProperty] private string _fontFamily;
    [ObservableProperty] private double _translatedFontSize;
    [ObservableProperty] private double _originalFontSize;
    [ObservableProperty] private string _translatedColor;
    [ObservableProperty] private string _originalColor;
    [ObservableProperty] private string _backgroundColor;
    [ObservableProperty] private double _backgroundOpacityPercent;
    [ObservableProperty] private double _historyOpacityPercent;
    [ObservableProperty] private bool _textShadow;
    [ObservableProperty] private string _hotkeyToggleRun;
    [ObservableProperty] private string _hotkeyToggleVisible;
    [ObservableProperty] private string _hotkeyToggleClickThrough;
    [ObservableProperty] private string _hotkeyClearHistory;

    /// <summary>Raised whenever a visual property changes, for live preview.</summary>
    public event Action<AppSettings>? PreviewRequested;

    public AppSettings Original => _original;

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApiKey) or nameof(Model) or nameof(ApiVersion) or nameof(ProxyUrl) or nameof(TargetLanguage)
            or nameof(HotkeyToggleRun) or nameof(HotkeyToggleVisible) or nameof(HotkeyToggleClickThrough) or nameof(HotkeyClearHistory))
        {
            return;
        }
        PreviewRequested?.Invoke(Build());
    }

    /// <summary>Builds a settings snapshot (API key not included; handled by caller).</summary>
    public AppSettings Build()
    {
        var s = _original.Clone();
        s.Model = string.IsNullOrWhiteSpace(Model) ? new AppSettings().Model : Model.Trim();
        s.ApiVersion = string.IsNullOrWhiteSpace(ApiVersion) ? "v1beta" : ApiVersion.Trim();
        s.ProxyUrl = string.IsNullOrWhiteSpace(ProxyUrl) ? null : ProxyUrl.Trim();
        s.UiLanguage = string.IsNullOrWhiteSpace(UiLanguage) ? "auto" : UiLanguage.Trim();
        s.TargetLanguage = ExtractCode(TargetLanguage);
        s.DisplayMode = DisplayMode;
        s.SubtitleOrder = SubtitleOrder;
        s.SubtitleAlignment = SubtitleAlignment;
        s.MaxHistoryEntries = Math.Clamp(MaxHistoryEntries, 10, 1000);
        s.FontFamily = FontFamily;
        s.TranslatedFontSize = TranslatedFontSize;
        s.OriginalFontSize = OriginalFontSize;
        s.TranslatedColor = TranslatedColor;
        s.OriginalColor = OriginalColor;
        s.BackgroundColor = BackgroundColor;
        s.BackgroundOpacity = Math.Clamp(BackgroundOpacityPercent / 100.0, 0.05, 1);
        s.HistoryOpacity = Math.Clamp(HistoryOpacityPercent / 100.0, 0.10, 1.0);
        s.TextShadow = TextShadow;
        s.HotkeyToggleRun = HotkeyToggleRun.Trim();
        s.HotkeyToggleVisible = HotkeyToggleVisible.Trim();
        s.HotkeyToggleClickThrough = HotkeyToggleClickThrough.Trim();
        s.HotkeyClearHistory = HotkeyClearHistory.Trim();
        return s;
    }

    /// <summary>Accepts either a bare code ("ja") or the combo display text ("日本語 (ja)").</summary>
    internal static string ExtractCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "zh-CN";
        text = text.Trim();
        int open = text.LastIndexOf('(');
        int close = text.LastIndexOf(')');
        if (open >= 0 && close > open) return text[(open + 1)..close].Trim();
        return text;
    }
}
