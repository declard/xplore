using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

namespace Xplore.App.ViewModels;

// "Open with" for the selected files: the full dialog and the remembered quick-open apps.
public sealed class OpenWithService
{
    private readonly IUserPrompts _prompts;
    private readonly OperationRunner _runner;
    private readonly Func<IReadOnlyList<ItemViewModel>> _selection;

    public OpenWithService(IUserPrompts prompts, OperationRunner runner, Func<IReadOnlyList<ItemViewModel>> selection)
    {
        _prompts = prompts;
        _runner = runner;
        _selection = selection;
        DialogCommand = new AsyncRelayCommand(OpenDialog, () => Targets.Count > 0);
    }

    public ICommand DialogCommand { get; }

    public async Task<IReadOnlyList<OpenWithApp>> QuickAppsAsync() =>
        Targets is [var first, ..] ? await OpenWithStore.ForAsync(first) : [];

    public Task LaunchAsync(OpenWithApp app) => _runner.Run(() => AssocHandlers.Launch(app, Targets));

    public Task ForgetAsync(OpenWithApp app) => OpenWithStore.ForgetAsync(_selection()[0].Entry.Path, app);

    // The selected files when there are only files and they all share one "Open with" key (extension or file name).
    private IReadOnlyList<string> Targets =>
        _selection() is var items && items.All(i => !i.Entry.IsDirectory) && items.Select(i => AppSettings.OpenWithKey(i.Entry.Path)).Distinct().Count() == 1
            ? [.. items.Select(i => i.Entry.Path)]
            : [];

    private Task OpenDialog() => _runner.RunAsync(async () =>
    {
        var files = Targets;
        var (app, useSystem) = await _prompts.ChooseOpenWithAsync(files[0]);
        if (useSystem)
        {
            files.ToList().ForEach(f => ShellVerbs.OpenWith(f, new WindowInteropHelper(Application.Current.MainWindow).Handle));
            return;
        }

        if (app is null)
            return;

        AssocHandlers.Launch(app, files);
        await OpenWithStore.RememberAsync(files[0], app);
    });
}
