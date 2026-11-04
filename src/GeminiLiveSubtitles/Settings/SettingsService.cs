using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeminiLiveSubtitles.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;

public sealed class SettingsService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GeminiLiveSubtitles.v1");
    private readonly string _path;

    public SettingsService(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeminiLiveSubtitles");
        _path = Path.Combine(Directory, "settings.json");
        Current = Load();
    }

    public string Directory { get; }
    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load settings, using defaults: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(tmp, _path, overwrite: true);
        Changed?.Invoke(settings);
    }

    /// <summary>Returns the decrypted API key, falling back to the GEMINI_API_KEY environment variable.</summary>
    public string? GetApiKey()
    {
        var key = Unprotect(Current.ProtectedApiKey);
        if (!string.IsNullOrWhiteSpace(key)) return key;
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain.Trim()), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to decrypt API key: {ex.Message}");
            return null;
        }
    }
}
