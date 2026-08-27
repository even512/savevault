using System.IO;
using System.Threading;

namespace SaveVault.Client.Services;

/// <summary>
/// Überwacht einen einzelnen Save-Ordner mit <see cref="FileSystemWatcher"/> und
/// <b>entprellt</b> die Ereignisse: eine Serie schnell aufeinanderfolgender Änderungen
/// (Spielstände werden oft in Bursts geschrieben) wird zu <em>einem</em>
/// <see cref="Changed"/>-Ereignis zusammengefasst, das erst nach einer Ruhephase feuert.
///
/// Robustheit: Bei <see cref="FileSystemWatcher.Error"/> (u. a. Puffer-Overflow) wird
/// sicherheitshalber ein <see cref="Changed"/> ausgelöst (damit ein voller Rescan folgt)
/// und die Überwachung – best effort – neu aktiviert.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly string _folder;
    private readonly TimeSpan _debounce;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    /// <summary>Wird (entprellt) ausgelöst, wenn sich im Ordner etwas geändert hat. Parameter = Ordnerpfad.</summary>
    public event Action<string>? Changed;

    /// <summary>Der überwachte Ordner.</summary>
    public string Folder => _folder;

    /// <summary>Ob die Überwachung aktiv läuft (Ordner existiert &amp; Watcher aktiviert).</summary>
    public bool IsWatching
    {
        get { lock (_lock) return _watcher is { EnableRaisingEvents: true }; }
    }

    public FolderWatcher(string folder, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = Path.GetFullPath(folder);
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        TryStart();
    }

    private void TryStart()
    {
        // Existiert der Ordner (noch) nicht, bleibt der Watcher inert – kein Absturz.
        if (!Directory.Exists(_folder))
            return;

        try
        {
            var watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnFsEvent;
            watcher.Created += OnFsEvent;
            watcher.Deleted += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            lock (_lock)
            {
                _watcher?.Dispose();
                _watcher = watcher;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Watcher konnte nicht gestartet werden; der periodische Rescan des Agents fängt das auf.
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Kick();

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Puffer-Overflow o. Ä.: sicherheitshalber ein Change-Signal geben und neu aufsetzen.
        Kick();
        lock (_lock)
        {
            if (_disposed)
                return;
            _watcher?.Dispose();
            _watcher = null;
        }
        TryStart();
    }

    /// <summary>Startet/verlängert das Entprell-Fenster.</summary>
    private void Kick()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? _)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
        }
        Changed?.Invoke(_folder);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        if (_watcher is not null)
        {
            _watcher.Changed -= OnFsEvent;
            _watcher.Created -= OnFsEvent;
            _watcher.Deleted -= OnFsEvent;
            _watcher.Renamed -= OnFsEvent;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}
