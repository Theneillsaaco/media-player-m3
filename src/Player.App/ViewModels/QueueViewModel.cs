using System.ComponentModel;
using System.Runtime.CompilerServices;
using Player.App.Models;

namespace Player.App.ViewModels;

/// <summary>The queue panel: rows in play order, how many there are and which one is playing.</summary>
public class QueueViewModel : INotifyPropertyChanged
{
    private int _revision = -1;

    public QueueViewModel()
    {
        AppServices.EnsureStarted();
        AppServices.Playback.Changed += Refresh;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackListModel Items { get; } = new();
    public int Count { get; private set => Set(ref field, value); }
    /// <summary>Position of the current track in the panel (play order), -1 if none.</summary>
    public int Position { get; private set => Set(ref field, value); } = -1;

    public void PlayItem(int position) => AppServices.Playback.PlayQueueItem(position);

    /// <summary>Drag and drop: the entry at <paramref name="source"/> ends up at <paramref name="target"/>.</summary>
    public void MoveItem(int source, int target) => AppServices.Playback.MoveQueueItem(source, target);

    public void RemoveItem(int position) => AppServices.Playback.RemoveQueueItem(position);

    public void ClearAll() => AppServices.Playback.ClearQueue();

    private void Refresh()
    {
        var p = AppServices.Playback;
        if (p.QueueRevision != _revision)
        {
            _revision = p.QueueRevision;
            Items.Replace(p.Ordered.Select(Rows.FromTrack));
        }
        Count = p.QueueCount;
        Position = p.QueuePosition;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
