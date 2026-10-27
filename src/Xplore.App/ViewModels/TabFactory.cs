using Xplore.Core.Model;
using Xplore.Core.Settings;

namespace Xplore.App.ViewModels;

public sealed class TabFactory(
    IFileSystemService fs,
    IUserPrompts prompts,
    BookmarkStore bookmarks,
    ActionHistoryStore history,
    OpenWithStore openWith,
    ISettings settings,
    PathFormat format)
{
    public TabViewModel Create(string? location) => new(fs, prompts, bookmarks, history, openWith, settings, format, location);
}
