using System.Windows;
using System.Windows.Threading;

namespace Xplore.App.ViewModels;

// Watches one folder and raises Elapsed once its changes have settled.
public sealed class FolderWatcher : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private FileSystemWatcher? _watcher;

    public FolderWatcher() =>
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Elapsed?.Invoke();
        };

    public event Action? Elapsed;

    public bool IsWatching => _watcher is not null;

    public void Watch(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (path is null)
            return;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
            };
            void Changed(object sender, FileSystemEventArgs e) => Application.Current?.Dispatcher.BeginInvoke(Restart);
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Changed += Changed;
            watcher.Renamed += (s, e) => Changed(s, e);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // happens
        }
    }

    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _watcher?.Dispose();
    }
}
