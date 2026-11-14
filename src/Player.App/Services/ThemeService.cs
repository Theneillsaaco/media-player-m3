using Player.Core.Theme;

namespace Player.App.Services;

/// <summary>What the UI needs to paint: the choices, whether it is dark and every color role as a JSON object.</summary>
internal readonly record struct ThemeSnapshot(
    string Mode, string Accent, string Variant, string Contrast, bool IsDark, string Tokens, bool Dynamic);

/// <summary>
/// Appearance: mode (light, dark or follow the desktop), accent color, scheme variant and contrast. Keeps the
/// choices in the settings table and turns them into color tokens with the Material solver in Player.Core.
/// With the dynamic option on, the source color is the one of the cover that plays (sent by QML) instead of
/// the accent. Used on the Qt thread only; the desktop watcher is the one piece that works in the background.
/// </summary>
internal sealed class ThemeService : IDisposable
{
    private const string DynamicKey = "theme.dynamic";
    private const int MaxCachedSchemes = 32;

    private readonly SafeSettings _settings;
    private readonly SystemColorScheme _system = new();
    private readonly Dictionary<string, string> _tokens = [];
    private string _mode, _accent, _variant, _contrast;
    private string _coverSeed = "";
    private bool _systemDark, _dynamic;

    public ThemeService(SafeSettings settings)
    {
        _settings = settings;
        _mode = Appearance.normalizeMode(settings.Get(Appearance.modeKey));
        _accent = Appearance.normalizeAccent(settings.Get(Appearance.accentKey));
        _variant = Appearance.normalizeVariant(settings.Get(Appearance.variantKey));
        _contrast = Appearance.normalizeContrast(settings.Get(Appearance.contrastKey));
        _dynamic = settings.Get(DynamicKey) == "1";
        FollowSystem();
        Snapshot = Build();
    }

    public ThemeSnapshot Snapshot { get; private set; }

    public void ChooseMode(string mode)
    {
        _mode = Appearance.normalizeMode(mode);
        _settings.Set(Appearance.modeKey, _mode);
        FollowSystem();
        Rebuild();
    }

    /// <summary>False when the text is not a "#rrggbb" color (nothing changes then).</summary>
    public bool ChooseAccent(string hex)
    {
        if (!Appearance.isAccent(hex))
            return false;

        _accent = Appearance.normalizeAccent(hex);
        _settings.Set(Appearance.accentKey, _accent);
        Rebuild();
        return true;
    }

    public void ChooseVariant(string variant)
    {
        _variant = Appearance.normalizeVariant(variant);
        _settings.Set(Appearance.variantKey, _variant);
        Rebuild();
    }

    public void ChooseContrast(string contrast)
    {
        _contrast = Appearance.normalizeContrast(contrast);
        _settings.Set(Appearance.contrastKey, _contrast);
        Rebuild();
    }

    /// <summary>Takes the colors from the cover that plays (on) or from the chosen accent (off).</summary>
    public void ChooseDynamic(bool on)
    {
        _dynamic = on;
        _settings.Set(DynamicKey, on ? "1" : "0");
        Rebuild();
    }

    /// <summary>
    /// The dominant color of the cover that plays, "" when it has none. True when the snapshot changed, which only
    /// happens while the dynamic option is on.
    /// </summary>
    public bool UseCoverColor(string hex)
    {
        var seed = Appearance.isAccent(hex) ? Appearance.normalizeAccent(hex) : "";
        if (seed == _coverSeed)
            return false;

        _coverSeed = seed;
        if (!_dynamic)
            return false;

        Rebuild();
        return true;
    }

    /// <summary>Picks up a change of the desktop's light/dark setting. True when the snapshot changed.</summary>
    public bool Poll()
    {
        if (_mode != "system")
            return false;

        var now = _system.IsDark ?? _systemDark;
        if (now == _systemDark)
            return false;

        _systemDark = now;
        Rebuild();
        return true;
    }

    public void Dispose() => _system.Dispose();

    private void Rebuild() => Snapshot = Build();

    private void FollowSystem()
    {
        if (_mode == "system")
        {
            _system.Start();
            _systemDark = _system.IsDark ?? _systemDark;
        }
        else
        {
            _system.Stop();
        }
    }

    private ThemeSnapshot Build()
    {
        var dark = Appearance.isDark(_mode, _systemDark);
        var seed = _dynamic && _coverSeed.Length > 0 ? _coverSeed : _accent;
        var key = $"{seed}|{dark}|{_variant}|{_contrast}";

        if (!_tokens.TryGetValue(key, out var tokens))
        {
            if (_tokens.Count >= MaxCachedSchemes)
                _tokens.Clear();

            tokens = _tokens[key] = Appearance.tokens(seed, dark, _variant, _contrast);
        }

        return new ThemeSnapshot(_mode, _accent, _variant, _contrast, dark, tokens, _dynamic);
    }
}
