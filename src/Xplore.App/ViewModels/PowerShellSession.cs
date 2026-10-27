using Xplore.Core.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Xplore.App.ViewModels;

// One long-lived PowerShell process. Each command is sent as three stdin lines (working directory, command text, selection);
// the process answers with a marker line on both stdout and stderr when it is done. All events are raised on the dispatcher.
public sealed partial class PowerShellSession(Dispatcher dispatcher) : IDisposable
{
    // Escape sequences (colours, window-title/progress codes) that programs emit; the output area shows plain text.
    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)")]
    private static partial Regex Escapes();

    private static string Script(string marker) => $$"""
        [Console]::OutputEncoding=[Text.Encoding]::UTF8;
        [Console]::InputEncoding=[Text.Encoding]::UTF8;
        $ProgressPreference='SilentlyContinue';
        $__m='{{marker}}';
        while($null -ne ($__d=[Console]::In.ReadLine()))
        {
            $__c=[Console]::In.ReadLine();
            $__s=[Console]::In.ReadLine();
            $selection=@(if($__s) { $__s -split '\\|' });
            try
            {
                Set-Location -LiteralPath $__d;
                Invoke-Expression $__c | Out-Host
            }
            catch { Write-Error $_ };
            [Console]::Out.WriteLine($__m);
            [Console]::Error.WriteLine($__m)
        }
        """;

    private Process? _process;
    private bool _doneOut;
    private bool _doneErr;
    private bool _cancelRequested;
    private bool _disposed;

    public event Action<string>? Line;

    public event Action? CommandFinished;

    // The argument tells whether the session was ended by Cancel.
    public event Action<bool>? Exited;

    public bool IsAlive => _process is { HasExited: false };

    // Throws Win32Exception or InvalidOperationException when PowerShell cannot be started.
    public void Start(string directory)
    {
        _doneOut = false;
        _doneErr = false;
        var marker = $"XPLORE-DONE-{Guid.NewGuid():N}";
        var info = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Script(marker) })
        {
            info.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(process, e.Data, marker, isError: false);
        process.ErrorDataReceived += (_, e) => OnLine(process, e.Data, marker, isError: true);
        process.Exited += (_, _) => dispatcher.BeginInvoke(() => OnExited(process));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    // Throws IOException when the process is gone.
    public void Send(string directory, string command, IEnumerable<string> selection)
    {
        if (_process is not { } process)
            throw new IOException(Loc.Get("Console.NotRunning"));

        _doneOut = false;
        _doneErr = false;
        process.StandardInput.WriteLine(directory);
        process.StandardInput.WriteLine(command);
        process.StandardInput.WriteLine(string.Join('|', selection));
        process.StandardInput.Flush();
    }

    // Killing the process is the only reliable way to stop a running command.
    public void Cancel()
    {
        if (_process is not { } process)
            return;

        _cancelRequested = true;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // nothing else can be done
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }

    private void OnLine(Process process, string? line, string marker, bool isError)
    {
        if (line is null)
            return;

        line = Escapes().Replace(line, "");
        dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(process, _process))
                return;

            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) {
                Line?.Invoke(line);
                return;
            }

            if (at > 0)
            {
                Line?.Invoke(line[..at]);
            }
            if (isError)
            {
                _doneErr = true;
            }
            else
            {
                _doneOut = true;
            }

            if (!_doneOut || !_doneErr)
                return;

            _doneOut = false;
            _doneErr = false;
            CommandFinished?.Invoke();
        });
    }

    private void OnExited(Process process)
    {
        if (!ReferenceEquals(process, _process))
            return;

        _process = null;
        process.Dispose();
        var cancelled = _cancelRequested;
        _cancelRequested = false;
        if (!_disposed)
        {
            Exited?.Invoke(cancelled);
        }
    }
}
