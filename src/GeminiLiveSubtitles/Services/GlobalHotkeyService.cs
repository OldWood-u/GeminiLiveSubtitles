using System.Windows.Input;
using System.Windows.Interop;
using GeminiLiveSubtitles.Interop;
using GeminiLiveSubtitles.Settings;

namespace GeminiLiveSubtitles.Services;

/// <summary>
/// Registers system-wide hotkeys via Win32 RegisterHotKey on a given window handle.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int IdToggleRun = 9001;
    private const int IdToggleVisible = 9002;
    private const int IdToggleClickThrough = 9003;
    private const int IdClearHistory = 9004;

    private readonly nint _hwnd;
    private readonly HwndSource _source;
    private readonly List<int> _registered = [];

    public GlobalHotkeyService(nint hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("Invalid window handle for hotkeys");
        _source.AddHook(WndProc);
    }

    public event Action? ToggleRunRequested;
    public event Action? ToggleVisibleRequested;
    public event Action? ToggleClickThroughRequested;
    public event Action? ClearHistoryRequested;

    public void Apply(AppSettings s)
    {
        UnregisterAll();
        TryRegister(IdToggleRun, s.HotkeyToggleRun);
        TryRegister(IdToggleVisible, s.HotkeyToggleVisible);
        TryRegister(IdToggleClickThrough, s.HotkeyToggleClickThrough);
        TryRegister(IdClearHistory, s.HotkeyClearHistory);
    }

    private void TryRegister(int id, string? spec)
    {
        if (!TryParseHotkey(spec, out uint mods, out uint vk)) return;
        if (NativeMethods.RegisterHotKey(_hwnd, id, mods | NativeMethods.MOD_NOREPEAT, vk))
        {
            _registered.Add(id);
            Log.Info($"Registered global hotkey [{spec}] (id={id})");
        }
        else
        {
            Log.Warn($"Failed to register global hotkey [{spec}] (may be in use by another app)");
        }
    }

    private void UnregisterAll()
    {
        foreach (int id in _registered)
            NativeMethods.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    /// <summary>Parses e.g. "Alt+S", "Ctrl+Shift+T", "F8".</summary>
    public static bool TryParseHotkey(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "ALT": modifiers |= NativeMethods.MOD_ALT; break;
                case "CTRL" or "CONTROL": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "SHIFT": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "WIN" or "WINDOWS" or "META": modifiers |= NativeMethods.MOD_WIN; break;
                default: return false;
            }
        }

        var keyToken = parts[^1];
        if (!Enum.TryParse<Key>(keyToken, ignoreCase: true, out var key) &&
            !(keyToken.Length == 1 && char.IsAsciiDigit(keyToken[0]) && Enum.TryParse("D" + keyToken, out key)))
        {
            return false;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return false;
        virtualKey = (uint)vk;
        return true;
    }

    public static string FormatKeyGesture(ModifierKeys mods, Key key)
    {
        if (key is Key.System) return "";
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return "";
        }

        var parts = new List<string>(4);
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var keyName = key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString();
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            switch ((int)wParam)
            {
                case IdToggleRun: ToggleRunRequested?.Invoke(); handled = true; break;
                case IdToggleVisible: ToggleVisibleRequested?.Invoke(); handled = true; break;
                case IdToggleClickThrough: ToggleClickThroughRequested?.Invoke(); handled = true; break;
                case IdClearHistory: ClearHistoryRequested?.Invoke(); handled = true; break;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
