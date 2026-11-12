using System.Windows.Threading;

namespace Xplore.App.Interaction;

// The shell's context-menu handlers are in-process COM objects that need a single-threaded apartment with a message pump, and can take seconds to load,
// so all of them live on one thread of their own: a menu, its commands and its disposal must all run here.
internal static class ShellThread
{
    private static readonly Lazy<Dispatcher> Loop = new(Start);

    public static Task<T> Run<T>(Func<T> work) => Loop.Value.InvokeAsync(work).Task;

    public static Task Run(Action work) => Loop.Value.InvokeAsync(work).Task;

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Shell menus",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
