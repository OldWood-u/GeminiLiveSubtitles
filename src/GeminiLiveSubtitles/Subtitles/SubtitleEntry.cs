using CommunityToolkit.Mvvm.ComponentModel;

namespace GeminiLiveSubtitles.Subtitles;

/// <summary>One utterance in the conversation history.</summary>
public sealed partial class SubtitleEntry : ObservableObject
{
    public SubtitleEntry(DateTimeOffset timestamp)
    {
        Timestamp = timestamp;
    }

    public DateTimeOffset Timestamp { get; }
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    [ObservableProperty] private string _original = "";
    [ObservableProperty] private string _translated = "";
    [ObservableProperty] private bool _isActive = true;

    public bool HasOriginal => Original.Length > 0;
    public bool HasTranslated => Translated.Length > 0;

    partial void OnOriginalChanged(string value) => OnPropertyChanged(nameof(HasOriginal));
    partial void OnTranslatedChanged(string value) => OnPropertyChanged(nameof(HasTranslated));
}
