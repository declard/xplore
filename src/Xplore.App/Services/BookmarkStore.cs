using System.Collections.ObjectModel;
using Xplore.Core.Settings;

namespace Xplore.App;

public sealed class BookmarkStore(IEnumerable<Bookmark> initial)
{
    public ObservableCollection<Bookmark> Items { get; } = new(initial);

    public event Action? Changed;

    public static async Task<BookmarkStore> LoadAsync() => new(await BookmarkRegistry.LoadAsync());

    public bool Contains(string path) => Items.Any(b => Same(b.Path, path));

    public Task ToggleAsync(string path)
    {
        if (Items.FirstOrDefault(b => Same(b.Path, path)) is { } existing)
        {
            Items.Remove(existing);
        }
        else
        {
            Items.Add(new Bookmark(NameOf(path), path));
        }

        Changed?.Invoke();
        return BookmarkRegistry.SaveAsync(Items.ToList());
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public Task RenameAsync(string path, string name)
    {
        if (Items.FirstOrDefault(b => Same(b.Path, path)) is not { } existing)
            return Task.CompletedTask;

        Items[Items.IndexOf(existing)] = existing with { Name = name };
        Changed?.Invoke();
        return BookmarkRegistry.SaveAsync(Items.ToList());
    }

    private static string NameOf(string path) =>
        Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
}
