using Xplore.Core.Model;

namespace Xplore.App.ViewModels;

// Recursive search below the current folder; results are streamed through Found while the search text changes.
public sealed class FolderSearch(IFileSystemService fs, IUserPrompts prompts, Func<string?> currentPath, Func<Task> restore)
    : ObservableObject, IDisposable
{
    private string _text = "";
    private CancellationTokenSource? _cts;

    public event Action? Started;
    public event Action<FsEntry>? Found;

    public bool IsSearching { get; private set; }

    // True while results are still being collected.
    public bool IsRunning
    {
        get;
        private set => Set(ref field, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value))
                return;

            SearchAsync(value).Forget();
        }
    }

    public async Task ResetAsync()
    {
        await (_cts?.CancelAsync() ?? Task.CompletedTask);
        IsSearching = false;
        if (_text.Length == 0)
            return;

        _text = "";
        OnPropertyChanged(nameof(Text));
    }

    public void Dispose() => _cts?.Cancel();

    private async Task SearchAsync(string pattern)
    {
        await (_cts?.CancelAsync() ?? Task.CompletedTask);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            if (IsSearching)
            {
                await restore();
            }

            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            await Task.Delay(350, cts.Token);
            IsSearching = true;
            IsRunning = true;
            Started?.Invoke();
            foreach (var root in currentPath() is { } path ? [path] : (await fs.DrivesAsync(cts.Token)).Select(d => d.Path))
            {
                await foreach (var e in fs.Search(root, pattern.Trim(), cts.Token))
                {
                    Found?.Invoke(e);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // as planned
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await prompts.ReportErrorAsync(ex.Message);
        }
        finally
        {
            if (_cts == cts)
            {
                IsRunning = false;
            }
        }
    }
}
