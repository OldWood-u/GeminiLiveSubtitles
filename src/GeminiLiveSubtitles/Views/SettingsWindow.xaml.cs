using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GeminiLiveSubtitles.Services;
using GeminiLiveSubtitles.ViewModels;
using WinForms = System.Windows.Forms;

namespace GeminiLiveSubtitles.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        ApiKeyBox.Password = vm.ApiKey;
    }

    /// <summary>True when closed via the Save button; false when cancelled/closed.</summary>
    public bool Saved { get; private set; }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) => _vm.ApiKey = ApiKeyBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.ApiKey))
        {
            System.Windows.MessageBox.Show(this, "请填写 Gemini API Key。", "GeminiLiveSubtitles",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string prop }) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape or Key.Enter) return;

        e.Handled = true;
        string value;
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            value = "";
        }
        else
        {
            value = GlobalHotkeyService.FormatKeyGesture(Keyboard.Modifiers, key);
            if (string.IsNullOrEmpty(value)) return;
        }

        switch (prop)
        {
            case nameof(SettingsViewModel.HotkeyToggleRun): _vm.HotkeyToggleRun = value; break;
            case nameof(SettingsViewModel.HotkeyToggleVisible): _vm.HotkeyToggleVisible = value; break;
            case nameof(SettingsViewModel.HotkeyToggleClickThrough): _vm.HotkeyToggleClickThrough = value; break;
            case nameof(SettingsViewModel.HotkeyClearHistory): _vm.HotkeyClearHistory = value; break;
        }
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string prop }) return;
        var current = prop switch
        {
            nameof(SettingsViewModel.TranslatedColor) => _vm.TranslatedColor,
            nameof(SettingsViewModel.OriginalColor) => _vm.OriginalColor,
            _ => _vm.BackgroundColor,
        };
        var c = OverlayViewModel.ParseColor(current, System.Windows.Media.Colors.White);
        using var dlg = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B),
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        var hex = $"#FF{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        switch (prop)
        {
            case nameof(SettingsViewModel.TranslatedColor): _vm.TranslatedColor = hex; break;
            case nameof(SettingsViewModel.OriginalColor): _vm.OriginalColor = hex; break;
            default: _vm.BackgroundColor = hex; break;
        }
    }
}
