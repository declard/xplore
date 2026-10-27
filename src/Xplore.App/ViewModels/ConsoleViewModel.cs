using Xplore.Core.Localization;
using Xplore.Core.Settings;
using Xplore.Core.Shell;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Windows.Threading;

namespace Xplore.App.ViewModels;

// A per-tab console backed by one long-lived PowerShell session.
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private const int MaxOutputLength = 400_000;

    private readonly Func<string?> _location;
    private readonly Func<IReadOnlyList<string>> _selection;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly StringBuilder _output = new();
    private readonly CommandHistory _history = new();
    private readonly Subject<string?> _edits = new();
    private readonly IDisposable _flush;
    private readonly PowerShellSession _session;
    private bool _disposed;

    public ConsoleViewModel(Func<string?> location, Func<IReadOnlyList<string>> selection)
    {
        (_location, _selection) = (location, selection);
        _session = new PowerShellSession(_dispatcher);
        _session.Line += Append;
        _session.CommandFinished += () => IsRunning = false;
        _session.Exited += OnExited;
        _flush = _edits
            .Buffer(TimeSpan.FromMilliseconds(60))
            .Where(batch => batch.Count > 0)
            .ObserveOn(new DispatcherSynchronizationContext(_dispatcher))
            .Subscribe(batch =>
            {
                foreach (var line in batch)
                {
                    if (line is null)
                    {
                        _output.Clear();
                    }
                    else
                    {
                        _output.AppendLine(line);
                    }
                }

                KeepOutputLengthManageable();

                OnPropertyChanged(nameof(Output));
            });
    }

    private void KeepOutputLengthManageable()
    {
        if (_output.Length > MaxOutputLength)
        {
            _output.Remove(0, _output.Length - MaxOutputLength / 2);
        }
    }

    public string Output => _output.ToString();

    public bool IsActive => _location() is not null;

    public bool IsRunning
    {
        get;
        private set => Set(ref field, value);
    }

    public bool IsExpanded
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            if (value) EnsureSession();
        }
    }

    public string Input
    {
        get;
        set => Set(ref field, value);
    } = "";

    public void NotifyLocationChanged() => OnPropertyChanged(nameof(IsActive));

    public void Toggle() => IsExpanded = !IsExpanded;

    public void Submit()
    {
        var command = Input.Trim();
        if (command.Length == 0 || IsRunning || _location() is not { } directory)
            return;

        Input = "";
        _history.Add(command);
        Append($"> {command}");

        if (command is "cls" or "clear")
        {
            _edits.OnNext(null);
            return;
        }

        if (!EnsureSession())
            return;

        IsRunning = true;
        try
        {
            _session.Send(directory, command, _selection().Select(PathFormat.Show));
        }
        catch (IOException)
        {
            IsRunning = false;
        }
    }

    // A fresh session is started right away when a running command is cancelled.
    public void Cancel() => _session.Cancel();

    public void PreviousCommand() => Recall(_history.Previous());

    public void NextCommand() => Recall(_history.Next());

    public void Dispose()
    {
        _disposed = true;
        _flush.Dispose();
        _session.Dispose();
    }

    private void Recall(string? command)
    {
        if (command is null)
            return;

        Input = command;
    }

    private bool EnsureSession()
    {
        if (_session.IsAlive)
            return true;

        if (_disposed)
            return false;

        IsRunning = false;
        try
        {
            _session.Start(_location() ?? Environment.CurrentDirectory);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Append(Loc.Format("Console.StartFailed", ex.Message));
            return false;
        }
    }

    private void OnExited(bool cancelled)
    {
        var wasRunning = IsRunning;
        IsRunning = false;
        if (cancelled)
        {
            Append("^C");
        }
        else if (wasRunning || IsExpanded)
        {
            Append(Loc.Get("Console.Ended"));
        }

        if (IsExpanded && wasRunning)
        {
            EnsureSession();
        }
    }

    private void Append(string line) => _edits.OnNext(line);
}
