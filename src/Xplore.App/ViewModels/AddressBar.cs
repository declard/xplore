using System.Windows.Input;
using Xplore.Core.Localization;
using Xplore.Core.Model;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

namespace Xplore.App.ViewModels;

// The editable address text, the breadcrumb trail and navigation by typed text.
public sealed class AddressBar : ObservableObject
{
    private readonly IFileSystemService _fs;
    private readonly IUserPrompts _prompts;
    private readonly PathFormat _format;
    private readonly Func<string?> _currentPath;
    private readonly Func<string?, Task> _navigate;

    public AddressBar(IFileSystemService fs, IUserPrompts prompts, PathFormat format, Func<string?> currentPath, Func<string?, Task> navigate)
    {
        _fs = fs;
        _prompts = prompts;
        _format = format;
        _currentPath = currentPath;
        _navigate = navigate;
        NavigateCommand = new AsyncRelayCommand(() => NavigateToText(Text));
    }

    public ICommand NavigateCommand { get; }

    public string Text
    {
        get;
        set => Set(ref field, value);
    } = TabViewModel.RootName;

    public IReadOnlyList<Crumb> Breadcrumbs
    {
        get;
        set => Set(ref field, value);
    } = [];

    public void Reset(string? path) => Text = _format.Show(path ?? TabViewModel.RootName);

    public async Task<IReadOnlyList<Crumb>> BuildCrumbsAsync(string? path, CancellationToken ct)
    {
        var storage = new Crumb(TabViewModel.RootName, null);
        if (path is null)
            return [storage];

        return [storage, .. (await _fs.BreadcrumbsAsync(path, ct)).Select(c => new Crumb(c.Name, c.Path))];
    }

    private static async Task<string?> TryResolve(string trimmed)
    {
        try
        {
            return await Shortcuts.ResolvePathAsync(Path.GetFullPath(Environment.ExpandEnvironmentVariables(trimmed)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task NavigateToText(string text)
    {
        var trimmed = text.Trim().Trim('"');
        if (trimmed.Length == 0
            || trimmed.Equals(TabViewModel.RootName, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(TabViewModel.RootId, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("This PC", StringComparison.OrdinalIgnoreCase))
        {
            await _navigate(null);
            return;
        }

        if (_fs.Canonical(trimmed) is { } virtualLocation)
        {
            await _navigate(virtualLocation);
            return;
        }

        var full = await TryResolve(trimmed);
        if (full is not null && await Task.Run(() => Directory.Exists(full)))
        {
            await _navigate(full);
            return;
        }

        await _prompts.ReportErrorAsync(Loc.Format("Error.PathNotFound", trimmed));
        Reset(_currentPath());
    }
}
