using System;
using System.IO;
using System.Threading;
using StepUpAdvanced.Core;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Watches a single config file for changes, debounces rapid-fire change
/// events into one logical "the file changed" notification, and supports
/// short suppression windows during programmatic saves.
/// </summary>
/// <remarks>
/// <para>
/// Wraps a <see cref="FileSystemWatcher"/> behind a clean event API.
/// Subscribers connect to <see cref="ConfigFileChanged"/> and receive
/// callbacks on the main thread (via the dispatcher passed to the
/// constructor), debounced to avoid duplicate fires when an editor saves
/// in two writes (some editors do truncate-then-write, which fires Changed
/// twice within a few ms).
/// </para>
/// <para>
/// <b>Suppression:</b> code that programmatically saves the config it's
/// watching can call <see cref="Suppress"/> to ignore the resulting
/// FileSystemWatcher event. Without this, every <c>ConfigStore.Save</c>
/// would round-trip through the watcher and re-trigger a load.
/// </para>
/// <para>
/// The class is thread-safe for the operations it cares about: the
/// <see cref="_suppressed"/> flag is <c>volatile</c>, so reads on the
/// FileSystemWatcher's thread-pool callback see writes from any thread
/// promptly; the suppress-clear timer is re-armable rather than spawning
/// new timers per call, avoiding allocation pressure.
/// </para>
/// </remarks>
internal sealed class DebouncedConfigWatcher : IDisposable
{
    private readonly string _filePath;
    private readonly Action<Action> _dispatchToMainThread;
    private readonly int _debounceMs;

    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private System.Threading.Timer? _suppressClearTimer;

    /// <summary>
    /// True while a suppression window is active. <c>volatile</c> because
    /// it's read from the FileSystemWatcher's thread-pool callback and
    /// written from arbitrary threads (the suppress-clear timer fires on
    /// the thread pool, callers may invoke <see cref="Suppress"/> from
    /// the main thread).
    /// </summary>
    private volatile bool _suppressed;

    /// <summary>
    /// Fired once per debounced file change, on the main thread.
    /// Subscribers should reload their config and react accordingly —
    /// the watcher itself doesn't know what to do.
    /// </summary>
    public event Action? ConfigFileChanged;

    /// <param name="filePath">
    ///   Full path to the config file to watch. The directory and filename
    ///   are split internally for the FileSystemWatcher.
    /// </param>
    /// <param name="dispatchToMainThread">
    ///   Mechanism for marshaling work onto the game's main thread. In
    ///   production this is <c>sapi.Event.EnqueueMainThreadTask</c>; in
    ///   tests it could be a simple synchronous invoker.
    /// </param>
    /// <param name="debounceMs">
    ///   Time in milliseconds to wait after the last change before firing
    ///   <see cref="ConfigFileChanged"/>. Default 150 ms — empirically
    ///   long enough to coalesce editor save sequences and short enough
    ///   to feel responsive when a user manually edits the file.
    /// </param>
    public DebouncedConfigWatcher(string filePath, Action<Action> dispatchToMainThread, int debounceMs = 150)
    {
        _filePath = filePath;
        _dispatchToMainThread = dispatchToMainThread;
        _debounceMs = debounceMs;
    }

    /// <summary>
    /// Begins watching the file. Logs a warning and returns <c>false</c>
    /// if the configured path can't be split into directory + filename
    /// components. Idempotent: calling <see cref="Start"/> twice is safe;
    /// the second call is a no-op.
    /// </summary>
    /// <param name="api">
    ///   Used only for logging. The watcher itself does not retain or use
    ///   the API for its mechanics.
    /// </param>
    public bool Start(ICoreAPI api)
    {
        if (_watcher != null) return true;

        string? directory = Path.GetDirectoryName(_filePath);
        string? filename = Path.GetFileName(_filePath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filename))
        {
            ModLog.Warning(api, "Could not initialize config file watcher: invalid path.");
            return false;
        }

        _watcher = new FileSystemWatcher(directory, filename)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _debounceTimer = new System.Timers.Timer(_debounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += (_, __) =>
        {
            // Marshal to main thread before firing the event so subscribers
            // don't need to think about threading.
            _dispatchToMainThread(() =>
            {
                if (_suppressed) return;
                ConfigFileChanged?.Invoke();
            });
        };

        // Re-armable single-shot timer for clearing the suppress flag.
        // Created once, reused for every Suppress() call — avoids allocating
        // a fresh Timer per programmatic save.
        _suppressClearTimer = new System.Threading.Timer(
            _ => _suppressed = false,
            state: null,
            dueTime: Timeout.Infinite,
            period: Timeout.Infinite);

        _watcher.Changed += (sender, args) =>
        {
            // Two suppression checkpoints, both intentional:
            //  1. Here: don't even start the debounce timer if suppressed.
            //  2. Inside the debounce callback: catch the case where the
            //     timer was already running when suppression was raised.
            if (_suppressed) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };

        _watcher.EnableRaisingEvents = true;
        ModLog.Event(api, "File watcher initialized for config auto-reloading.");
        return true;
    }

    /// <summary>
    /// Suppresses watcher notifications for <paramref name="durationMs"/>.
    /// Used by code that programmatically saves the watched file, to avoid
    /// the round-trip of save → FileSystemWatcher event → reload.
    /// </summary>
    /// <remarks>
    /// Repeated calls re-arm the suppress-clear timer, so the suppression
    /// window extends from the most recent call rather than expiring on
    /// the first scheduled clear. This is the right semantics: if you
    /// save twice in quick succession, you want both saves suppressed.
    /// </remarks>
    public void Suppress(int durationMs)
    {
        _suppressed = true;
        _suppressClearTimer?.Change(durationMs, Timeout.Infinite);
    }

    /// <summary>
    /// Disposes the underlying FileSystemWatcher and timers. Safe to call
    /// even if <see cref="Start"/> was never invoked or failed.
    /// </summary>
    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _suppressClearTimer?.Dispose();
        _watcher?.Dispose();
    }
}
