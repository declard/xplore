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
    PathFormat format,
    TimestampFormat time)
{
    public TabViewModel Create(string? location) => new(fs, prompts, bookmarks, history, openWith, settings, format, time, location);
}
