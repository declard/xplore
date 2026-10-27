using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;

namespace Xplore.App;

public static class ClipboardHelper
{
    private const string DropEffect = "Preferred DropEffect";
    private const int MoveEffect = 2;
    private const int CopyEffect = 5;

    public static Task SetFilesAsync(IEnumerable<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange([.. paths]);
        return OnUi(() =>
        {
            var data = new DataObject();
            data.SetFileDropList(list);
            data.SetData(DropEffect, new MemoryStream(BitConverter.GetBytes(cut ? MoveEffect : CopyEffect)));
            Clipboard.SetDataObject(data, true);
        });
    }

    // Synchronous because command availability (CanExecute) cannot wait.
    public static bool HasFiles => Try(Clipboard.ContainsFileDropList);

    public static Task<(IReadOnlyList<string> Paths, bool Cut)> GetFilesAsync() =>
        OnUi(() =>
        {
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            var cut = Clipboard.GetDataObject()?.GetData(DropEffect) is MemoryStream s && s.ToArray() is { Length: >= 4 } b
                      && (BitConverter.ToInt32(b) & MoveEffect) != 0 && (BitConverter.ToInt32(b) & 1) == 0;
            return ((IReadOnlyList<string>)paths, cut);
        });

    public static Task SetTextAsync(string text) => OnUi(() => Clipboard.SetText(text));

    public static Task ClearAsync() => OnUi(Clipboard.Clear);

    // The clipboard belongs to the UI thread; calls are scheduled there as dispatcher operations.
    private static Task<T> OnUi<T>(Func<T> f) => Application.Current.Dispatcher.InvokeAsync(() => Try(f)).Task;

    private static Task OnUi(Action a) =>
        OnUi(() =>
        {
            a();
            return 0;
        });

    private static T Try<T>(Func<T> f)
    {
        try
        {
            return f();
        }
        catch (COMException)
        {
            return default!;
        }
    }
}
