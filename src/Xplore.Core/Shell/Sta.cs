namespace Xplore.Core.Shell;

// COM objects (WScript.Shell, Shell.Application) need a single-threaded apartment, which thread-pool threads are not. File-system calls, including the Visual Basic shell file operations, use the thread pool.
internal static class Sta
{
    public static Task<T> Run<T>(Func<T> work)
    {
        TaskCompletionSource<T> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    public static Task Run(Action work) => Run(() =>
    {
        work();
        return 0;
    });
}
