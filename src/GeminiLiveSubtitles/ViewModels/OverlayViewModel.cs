using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GeminiLiveSubtitles.Localization;
using GeminiLiveSubtitles.Settings;
using GeminiLiveSubtitles.Subtitles;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace GeminiLiveSubtitles.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    public OverlayViewModel(ObservableCollection<SubtitleEntry> entries)
    {
        Entries = entries;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EmptyHintVisibility));
        Loc.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RunButtonText));
            OnPropertyChanged(nameof(OrderButtonText));
            OnPropertyChanged(nameof(AlignButtonText));
        };
    }

    public ObservableCollection<SubtitleEntry> Entries { get; }

    [ObservableProperty] private string _originalText = "";
    [ObservableProperty] private string _translatedText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isStatusVisible;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private string _targetLanguage = "zh-CN";

    [ObservableProperty] private DisplayMode _displayMode = DisplayMode.Bilingual;
    [ObservableProperty] private SubtitleOrder _subtitleOrder = SubtitleOrder.TranslationFirst;
    [ObservableProperty] private SubtitleAlignment _subtitleAlignment = SubtitleAlignment.Left;
    [ObservableProperty] private MediaFontFamily _fontFamily = new("Microsoft YaHei UI");
    [ObservableProperty] private double _translatedFontSize = 24;
    [ObservableProperty] private double _originalFontSize = 15;
    [ObservableProperty] private System.Windows.Media.Brush _translatedBrush = System.Windows.Media.Brushes.White;
    [ObservableProperty] private System.Windows.Media.Brush _originalBrush = System.Windows.Media.Brushes.LightGray;
    [ObservableProperty] private System.Windows.Media.Brush _backgroundBrush = new SolidColorBrush(MediaColor.FromArgb(184, 16, 18, 22));
    [ObservableProperty] private double _historyOpacity = 0.50;
    [ObservableProperty] private double _shadowOpacity = 1;
    [ObservableProperty] private bool _isVrCaptureMode;

    public bool ShowOriginal => DisplayMode != DisplayMode.TranslationOnly;
    public bool ShowTranslated => DisplayMode != DisplayMode.OriginalOnly;
    public bool IsTranslationFirst => SubtitleOrder == SubtitleOrder.TranslationFirst;
    public bool IsOriginalFirst => SubtitleOrder == SubtitleOrder.OriginalFirst;
    public TextAlignment TextAlignment => SubtitleAlignment == SubtitleAlignment.Center ? TextAlignment.Center : TextAlignment.Left;

    public Visibility EmptyHintVisibility => Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string RunButtonText => I18n.Get(IsRunning ? "Btn_Pause" : "Btn_Start");
    public string OrderButtonText => I18n.Get(IsTranslationFirst ? "Btn_OrderTransFirst" : "Btn_OrderOrigFirst");
    public string AlignButtonText => I18n.Get(SubtitleAlignment == SubtitleAlignment.Center ? "Btn_AlignCenter" : "Btn_AlignLeft");

    partial void OnDisplayModeChanged(DisplayMode value)
    {
        OnPropertyChanged(nameof(ShowOriginal));
        OnPropertyChanged(nameof(ShowTranslated));
    }

    partial void OnSubtitleOrderChanged(SubtitleOrder value)
    {
        OnPropertyChanged(nameof(IsTranslationFirst));
        OnPropertyChanged(nameof(IsOriginalFirst));
        OnPropertyChanged(nameof(OrderButtonText));
    }

    partial void OnSubtitleAlignmentChanged(SubtitleAlignment value)
    {
        OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(AlignButtonText));
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RunButtonText));

    public void ApplyStyle(AppSettings s)
    {
        DisplayMode = s.DisplayMode;
        SubtitleOrder = s.SubtitleOrder;
        SubtitleAlignment = s.SubtitleAlignment;
        TargetLanguage = s.TargetLanguage;
        IsVrCaptureMode = s.IsVrCaptureMode;
        try { FontFamily = new MediaFontFamily(s.FontFamily); } catch { /* keep previous */ }
        TranslatedFontSize = Math.Clamp(s.TranslatedFontSize, 8, 120);
        OriginalFontSize = Math.Clamp(s.OriginalFontSize, 8, 120);

        TranslatedBrush = MakeBrush(s.TranslatedColor, Colors.White);
        OriginalBrush = MakeBrush(s.OriginalColor, Colors.LightGray);
        var bg = ParseColor(s.BackgroundColor, MediaColor.FromRgb(16, 18, 22));
        bg.A = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0.05, 1) * 255);
        BackgroundBrush = Freeze(new SolidColorBrush(bg));
        ShadowOpacity = s.TextShadow ? 1 : 0;
        HistoryOpacity = Math.Clamp(s.HistoryOpacity, 0.10, 1.0);
    }

    internal static MediaColor ParseColor(string? text, MediaColor fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try { return (MediaColor)MediaColorConverter.ConvertFromString(text.Trim()); }
        catch { return fallback; }
    }

    private static SolidColorBrush MakeBrush(string? text, MediaColor fallback) =>
        Freeze(new SolidColorBrush(ParseColor(text, fallback)));

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
