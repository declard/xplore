namespace Xplore.Core.Settings;

// Bookmarked folders, stored in bookmarks.yaml next to the settings file as a list in display order.
public sealed class BookmarkRegistry(string filePath)
{
    public async Task<IReadOnlyList<Bookmark>> LoadAsync() => await YamlFile.ReadAsync<List<Bookmark>>(filePath) ?? [];

    public Task SaveAsync(IReadOnlyList<Bookmark> bookmarks) => YamlFile.WriteAsync(filePath, bookmarks);
}
