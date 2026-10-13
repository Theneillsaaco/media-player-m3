using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Player.App.ViewModels;

/// <summary>
/// Appearance for QML. Instantiated once from Main.qml as <c>ThemeViewModel { id: themeVm }</c>; the Theme
/// component there turns <see cref="TokensJson"/> into color properties. The Choose methods are not called
/// SetX on purpose: the bridge would read SetMode as the setter of the Mode property.
/// </summary>
public class ThemeViewModel : INotifyPropertyChanged
{
    public ThemeViewModel()
    {
        AppServices.EnsureStarted();
        Sync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"system", "light" or "dark".</summary>
    public string Mode { get; private set => Set(ref field, value); } = "system";
    /// <summary>Source color, "#rrggbb".</summary>
    public string Accent { get; private set => Set(ref field, value); } = "#6750a4";
    /// <summary>"neutral", "tonalSpot", "vibrant", "expressive" or "content".</summary>
    public string Variant { get; private set => Set(ref field, value); } = "tonalSpot";
    /// <summary>"standard", "medium" or "high".</summary>
    public string Contrast { get; private set => Set(ref field, value); } = "standard";
    /// <summary>The mode after resolving "system".</summary>
    public bool IsDark { get; private set => Set(ref field, value); }
    /// <summary>A JSON object with every Material color role as "#rrggbb".</summary>
    public string TokensJson { get; private set => Set(ref field, value); } = "{}";

    public void ChooseMode(string mode) { AppServices.Theme.ChooseMode(mode); Sync(); }
    public void ChooseAccent(string hex) { AppServices.Theme.ChooseAccent(hex); Sync(); }
    public void ChooseVariant(string variant) { AppServices.Theme.ChooseVariant(variant); Sync(); }
    public void ChooseContrast(string contrast) { AppServices.Theme.ChooseContrast(contrast); Sync(); }

    /// <summary>Follows the desktop's light/dark setting. QML calls it from a Timer while the mode is "system".</summary>
    public void Poll()
    {
        if (AppServices.Theme.Poll())
            Sync();
    }

    private void Sync()
    {
        var s = AppServices.Theme.Snapshot;
        Mode = s.Mode;
        Accent = s.Accent;
        Variant = s.Variant;
        Contrast = s.Contrast;
        IsDark = s.IsDark;
        TokensJson = s.Tokens;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
