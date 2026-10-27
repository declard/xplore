using Xplore.App.ViewModels;
using Xplore.Core.Settings;

namespace Xplore.App;

public interface IUserPrompts
{
    Task ReportErrorAsync(string message);
    Task<bool> ConfirmAsync(string question);
    Task<bool> ConfirmDeleteAsync(IReadOnlyList<ItemViewModel> items, bool permanent);
    Task<(OpenWithApp? App, bool UseSystemDialog)> ChooseOpenWithAsync(string path);
    Task<string?> AskNewItemNameAsync(string dir, bool isFolder);
}
