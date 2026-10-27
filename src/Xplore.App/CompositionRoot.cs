using Microsoft.Extensions.DependencyInjection;
using Xplore.App.ViewModels;
using Xplore.Core.Localization;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Settings;

namespace Xplore.App;

// The one place where the application's services are registered. What needs the disk is loaded first so that the container itself is built synchronously.
public static class CompositionRoot
{
    public static async Task<ServiceProvider> BuildAsync()
    {
        var settings = new SettingsService();
        await settings.ReloadAsync();
        Loc.Apply(settings.Current.Language);

        var bookmarks = await BookmarkStore.LoadAsync(new BookmarkRegistry(AppFolder.File("bookmarks.yaml")));
        var sessions = new SessionStore(AppFolder.File("session.yaml"));
        var session = settings.Current.StartLocation?.Type == StartMode.Restore ? await sessions.LoadAsync() : null;
        var start = await Task.Run(() => StartTabs.Resolve(settings.Current, session));

        return new ServiceCollection()
            .AddSingleton<ISettings>(settings)
            .AddSingleton(bookmarks)
            .AddSingleton(sessions)
            .AddSingleton(start)
            .AddSingleton(new OpenWithRegistry(AppFolder.File("openWith.yaml")))
            .AddSingleton<PathFormat>()
            .AddSingleton<IUserPrompts, WpfPrompts>()
            .AddSingleton<FileLocationProvider>()
            .AddSingleton<ILocationProvider, RecycleBinLocationProvider>()
            .AddSingleton<IFileSystemService, FileSystemService>()
            .AddSingleton<OpenWithStore>()
            .AddSingleton<ActionHistoryStore>()
            .AddSingleton<TabFactory>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
