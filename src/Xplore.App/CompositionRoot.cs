using Xplore.App.ViewModels;
using Xplore.Core.Localization;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Settings;

namespace Xplore.App;

// The one place where the application's services are created and wired together.
public static class CompositionRoot
{
    public static async Task<MainViewModel> CreateAsync()
    {
        var settings = new SettingsService();
        await settings.ReloadAsync();
        Loc.Apply(settings.Current.Language);

        var format = new PathFormat(settings);
        var prompts = new WpfPrompts();
        var fs = new FileSystemService(new FileLocationProvider(), [new RecycleBinLocationProvider()]);
        var bookmarks = await BookmarkStore.LoadAsync(new BookmarkRegistry(AppFolder.File("bookmarks.yaml")));
        var openWith = new OpenWithStore(new OpenWithRegistry(AppFolder.File("openWith.yaml")));
        var history = new ActionHistoryStore(prompts, settings, format);
        var tabs = new TabFactory(fs, prompts, bookmarks, history, openWith, settings, format);

        var sessions = new SessionStore(AppFolder.File("session.yaml"));
        var session = settings.Current.StartLocation?.Type == StartMode.Restore ? await sessions.LoadAsync() : null;
        var start = await Task.Run(() => StartTabs.Resolve(settings.Current, session));
        return new(settings, format, history, tabs, sessions, start);
    }
}
