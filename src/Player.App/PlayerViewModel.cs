using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.Audio;
using Player.Core;

namespace PlayerApp;

/// <summary>
/// What QML sees. Instantiated from Main.qml as <c>PlayerViewModel { id: player }</c>.
/// All state is refreshed by <see cref="Poll"/>, which QML calls from a Timer on the Qt
/// thread, so no property change is ever raised from the engine's threads.
/// </summary>
[Qt.Include]
public class PlayerViewModel : INotifyPropertyChanged
{
    private readonly IAudioEngine _engine = new MpvEngine();
    private double _position, _duration;
    private bool _paused = true, _loaded, _sawActive;
    private string _status = "Pega un enlace o una ruta y pulsa Cargar";

    public PlayerViewModel() => AppDomain.CurrentDomain.ProcessExit += (_, _) => _engine.Dispose();

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Position { get => _position; private set => Set(ref _position, value); }
    public double Duration { get => _duration; private set => Set(ref _duration, value); }
    public bool Paused { get => _paused; private set => Set(ref _paused, value); }
    public bool Loaded { get => _loaded; private set => Set(ref _loaded, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public void Load(string source)
    {
        source = source.Trim();
        if (source.Length == 0) return;
        _sawActive = false;
        Loaded = true;
        Position = 0;
        Duration = 0;
        Status = "Cargando…";
        _engine.Load(source);
        Poll();
    }

    public void Toggle()
    {
        if (!Loaded) return;
        _engine.SetPaused(!_engine.Paused);
        Poll();
    }

    public void Seek(double seconds)
    {
        if (!Loaded) return;
        _engine.Seek(seconds);
        Poll();
    }

    public void SetVolume(int percent) => _engine.SetVolume(percent);

    public void Poll()
    {
        var error = _engine.DrainEvents();
        if (error.Length > 0)
        {
            Loaded = false;
            Status = "Error: " + error;
            return;
        }

        Position = _engine.Position;
        Duration = _engine.Duration;
        Paused = _engine.Paused;

        // idle-active can still read "yes" for a moment right after loadfile.
        if (!_engine.Idle) { _sawActive = true; if (Duration > 0) Status = "Reproduciendo"; }
        else if (_sawActive) { Loaded = false; Position = 0; Status = "Terminó"; }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
