namespace Xplore.Core.Settings;

// Bookmarked folders, stored in bookmarks.yaml next to the settings file as a list in display order.
public static class BookmarkRegistry
{
    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.FilePath)!, "bookmarks.yaml");

    public static async Task<IReadOnlyList<Bookmark>> LoadAsync() => await YamlFile.ReadAsync<List<Bookmark>>(FilePath) ?? [];

    public static Task SaveAsync(IReadOnlyList<Bookmark> bookmarks) => YamlFile.WriteAsync(FilePath, bookmarks);
}
