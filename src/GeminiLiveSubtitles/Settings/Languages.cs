namespace GeminiLiveSubtitles.Settings;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => $"{Name} ({Code})";
}

public static class Languages
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("zh-CN", "简体中文"),
        new("zh-TW", "繁體中文"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("pt", "Português"),
        new("ru", "Русский"),
        new("it", "Italiano"),
        new("ar", "العربية"),
        new("hi", "हिन्दी"),
        new("th", "ไทย"),
        new("vi", "Tiếng Việt"),
        new("id", "Bahasa Indonesia"),
    ];

    public static string NameOf(string code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code;
}
