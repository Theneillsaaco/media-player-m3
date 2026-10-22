using Player.Core;
using Qt.Quick;

// A crash that comes from managed code leaves its trace in ~/.local/state/player/player.log before the process ends.
AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("crash", "unhandled exception", e.ExceptionObject as Exception);
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Warn("crash", "unobserved task exception", e.Exception);
    e.SetObserved();
};

// Initialize Qt
Qml.LoadFromRootModule("Main");
Qml.WaitForExit();
