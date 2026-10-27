using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Xplore.App.ViewModels;

// Runs a user-triggered operation and turns the failures an operation can expect into an error prompt.
public sealed class OperationRunner(IUserPrompts prompts)
{
    public Task Run(Action action) =>
        RunAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    public async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // it's all right
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception or COMException)
        {
            await prompts.ReportErrorAsync(ex.Message);
        }
    }
}
