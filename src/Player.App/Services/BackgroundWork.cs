using System.Collections.Concurrent;
using Player.Core;

namespace Player.App.Services;


/// <summary>
/// Runs work on the thread pool and leaves its result in a queue; <see cref="Drain"/>, called from the
/// view model's poll on the Qt thread, applies it. No property is ever changed from a foreign thread.
/// </summary>
internal sealed class BackgroundWork
{
    private readonly ConcurrentQueue<Action> _ui = new();
    private int _jobs; // only touched on the Qt thread
    
    public bool Busy => _jobs > 0;

    public void Run<T>(Task<T> task, Action<T> done, Action<Exception> failed)
    {
        _jobs++;
        _ = task.ContinueWith(t => _ui.Enqueue(() =>
        {
            _jobs--;
            if (t.IsFaulted)
                failed(t.Exception!.GetBaseException());
            else if (t.IsCompletedSuccessfully)
                done(t.Result);
        }), TaskScheduler.Default);
    }
    
    public void Drain()
    {
        while (_ui.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error("ui", "background result", ex); }
        }
    }
}