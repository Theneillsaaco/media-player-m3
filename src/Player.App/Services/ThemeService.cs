using Player.Core;
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

    private readonly Func<string, string?> _read;
    private readonly Action<string, string> _write;
    private readonly SystemColorScheme _system = new();
    private readonly Dictionary<string, string> _tokens = [];
    private string _mode, _accent, _variant, _contrast;
    private string _coverSeed = "";
    private bool _systemDark, _dynamic;

    public ThemeService(Func<string, string?> read, Action<string, string> write)
    {
        (_read, _write) = (read, write);
        _mode = Appearance.normalizeMode(Read(Appearance.modeKey));
        _accent = Appearance.normalizeAccent(Read(Appearance.accentKey));
        _variant = Appearance.normalizeVariant(Read(Appearance.variantKey));
        _contrast = Appearance.normalizeContrast(Read(Appearance.contrastKey));
        _dynamic = Read(DynamicKey) == "1";
        FollowSystem();
        Snapshot = Build();
    }

    public ThemeSnapshot Snapshot { get; private set; }

    public void ChooseMode(string mode)
    {
        _mode = Appearance.normalizeMode(mode);
        Save(Appearance.modeKey, _mode);
        FollowSystem();
        Snapshot = Build();
    }

    /// <summary>False when the text is not a "#rrggbb" color (nothing changes then).</summary>
    public bool ChooseAccent(string hex)
    {
        if (!Appearance.isAccent(hex))
            return false;

        _accent = Appearance.normalizeAccent(hex);
        Save(Appearance.accentKey, _accent);
        Snapshot = Build();
        return true;
    }

    public void ChooseVariant(string variant)
    {
        _variant = Appearance.normalizeVariant(variant);
        Save(Appearance.variantKey, _variant);
        Snapshot = Build();
    }

    public void ChooseContrast(string contrast)
    {
        _contrast = Appearance.normalizeContrast(contrast);
        Save(Appearance.contrastKey, _contrast);
        Snapshot = Build();
    }

    /// <summary>Takes the colors from the cover that plays (on) or from the chosen accent (off).</summary>
    public void ChooseDynamic(bool on)
    {
        _dynamic = on;
        Save(DynamicKey, on ? "1" : "0");
        Snapshot = Build();
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

        Snapshot = Build();
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
        Snapshot = Build();
        return true;
    }

    public void Dispose() => _system.Dispose();

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
            if (_tokens.Count >= 32)
                _tokens.Clear();

            tokens = _tokens[key] = Appearance.tokens(seed, dark, _variant, _contrast);
        }

        return new ThemeSnapshot(_mode, _accent, _variant, _contrast, dark, tokens, _dynamic);
    }

    private string? Read(string key)
    {
        try { return _read(key); }
        catch (Exception ex)
        {
            Log.Warn("theme", "read " + key, ex);
            return null;
        }
    }

    private void Save(string key, string value)
    {
        try { _write(key, value); }
        catch (Exception ex) { Log.Warn("theme", "save " + key, ex); }
    }
}
