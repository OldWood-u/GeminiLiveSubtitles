using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace GeminiLiveSubtitles;

/// <summary>Tiny thread-safe file logger. Keeps one file per day, deletes files older than 7 days.</summary>
public static partial class Log
{
    private static readonly object Gate = new();
    private static string? _dir;

    public static bool EchoToConsole { get; set; }

    public static void Initialize(string directory)
    {
        _dir = Path.Combine(directory, "logs");
        Directory.CreateDirectory(_dir);
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)) File.Delete(f);
        }
        catch { /* best effort */ }
    }

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERR", ex is null ? msg : $"{msg}: {ex}");

    /// <summary>Masks anything that looks like an API key (query string or bare token).</summary>
    public static string Redact(string s) => KeyRegex().Replace(s, "key=***");

    [GeneratedRegex(@"key=[^&\s""]+")]
    private static partial Regex KeyRegex();

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {Redact(msg)}";
        Debug.WriteLine(line);
        if (EchoToConsole) Console.WriteLine(line);
        if (_dir is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine); }
            catch { /* never throw from logging */ }
        }
    }
}
