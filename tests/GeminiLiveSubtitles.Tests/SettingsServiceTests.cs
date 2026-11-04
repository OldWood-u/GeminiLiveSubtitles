using System.IO;
using GeminiLiveSubtitles.Settings;
using GeminiLiveSubtitles.ViewModels;

namespace GeminiLiveSubtitles.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gls-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Defaults_When_No_File()
    {
        var svc = new SettingsService(_dir);
        Assert.Equal("zh-CN", svc.Current.TargetLanguage);
        Assert.Equal("gemini-3.5-live-translate-preview", svc.Current.Model);
        Assert.Equal(DisplayMode.Bilingual, svc.Current.DisplayMode);
    }

    [Fact]
    public void RoundTrips_And_Encrypts_ApiKey()
    {
        var svc = new SettingsService(_dir);
        var s = svc.Current.Clone();
        s.TargetLanguage = "ja";
        s.DisplayMode = DisplayMode.TranslationOnly;
        s.BackgroundOpacity = 0.6;
        s.HistoryOpacity = 0.42;
        s.ProtectedApiKey = SettingsService.Protect("secret-key-123");
        svc.Save(s);

        var raw = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("secret-key-123", raw);
        Assert.Contains("TranslationOnly", raw);

        var reloaded = new SettingsService(_dir);
        Assert.Equal("ja", reloaded.Current.TargetLanguage);
        Assert.Equal(DisplayMode.TranslationOnly, reloaded.Current.DisplayMode);
        Assert.Equal(0.6, reloaded.Current.BackgroundOpacity);
        Assert.Equal(0.42, reloaded.Current.HistoryOpacity);
        Assert.Equal("secret-key-123", reloaded.GetApiKey());
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        var svc = new SettingsService(_dir);
        Assert.Equal("zh-CN", svc.Current.TargetLanguage);
    }

    [Theory]
    [InlineData("日本語 (ja)", "ja")]
    [InlineData("pl", "pl")]
    [InlineData("  ", "zh-CN")]
    [InlineData("简体中文 (zh-CN)", "zh-CN")]
    public void ExtractCode_Parses_Combo_Text(string input, string expected) =>
        Assert.Equal(expected, SettingsViewModel.ExtractCode(input));

    [Theory]
    [InlineData("Alt+S", true)]
    [InlineData("Ctrl+Shift+T", true)]
    [InlineData("F9", true)]
    [InlineData("", false)]
    [InlineData("InvalidKey", false)]
    public void TryParseHotkey_Validates_Input(string spec, bool valid) =>
        Assert.Equal(valid, GeminiLiveSubtitles.Services.GlobalHotkeyService.TryParseHotkey(spec, out _, out _));
}
