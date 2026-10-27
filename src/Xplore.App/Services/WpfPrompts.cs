using Xplore.Core.Localization;
using System.Windows;
using Xplore.App.ViewModels;
using Xplore.Core.Settings;

namespace Xplore.App;

public sealed class WpfPrompts : IUserPrompts
{
    // Modal dialogs always open from a dispatcher operation, so callers on any thread can await them and the dialog never starts inside the caller's own stack.
    private static Task<T> OnUi<T>(Func<T> show) => Application.Current.Dispatcher.InvokeAsync(show).Task;

    public Task ReportErrorAsync(string message) =>
        OnUi(() => MessageBox.Show(message, Loc.Get("App.Name"), MessageBoxButton.OK, MessageBoxImage.Warning));

    public Task<bool> ConfirmAsync(string question) =>
        OnUi(() => MessageBox.Show(question, Loc.Get("App.Name"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);

    public Task<bool> ConfirmDeleteAsync(IReadOnlyList<ItemViewModel> items, bool permanent) =>
        OnUi(() => new DeleteConfirmWindow(items, permanent) { Owner = Application.Current.MainWindow }.ShowDialog() == true);

    public Task<string?> AskNewItemNameAsync(string dir, bool isFolder) =>
        OnUi(() => new NewItemWindow(dir, isFolder) { Owner = Application.Current.MainWindow } is var window && window.ShowDialog() == true ? window.ItemName : null);

    public Task<(OpenWithApp? App, bool UseSystemDialog)> ChooseOpenWithAsync(string path) =>
        OnUi<(OpenWithApp?, bool)>(() =>
        {
            var window = new OpenWithWindow(path) { Owner = Application.Current.MainWindow };
            return window.ShowDialog() == true ? (window.Chosen, window.UseSystemDialog) : (null, false);
        });
}
