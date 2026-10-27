using Xplore.Core.Settings;

namespace Xplore.App;

public static class OpenWithStore
{
    private static readonly SemaphoreSlim UpdateGate = new(1, 1);

    public static async Task<IReadOnlyList<OpenWithApp>> ForAsync(string path) =>
        (await OpenWithRegistry.LoadAsync()).GetValueOrDefault(AppSettings.OpenWithKey(path)) ?? [];

    public static Task RememberAsync(string path, OpenWithApp app) =>
        UpdateAsync(path, apps => apps.Where(a => !Same(a, app)).Prepend(app).ToList());

    public static Task ForgetAsync(string path, OpenWithApp app) =>
        UpdateAsync(path, apps => apps.Where(a => !Same(a, app)).ToList());

    private static bool Same(OpenWithApp a, OpenWithApp b) => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

    private static async Task UpdateAsync(string path, Func<IReadOnlyList<OpenWithApp>, IReadOnlyList<OpenWithApp>> change)
    {
        await UpdateGate.WaitAsync();
        try
        {
            var current = await OpenWithRegistry.LoadAsync();
            var key = AppSettings.OpenWithKey(path);
            var all = current
                .Where(kv => kv.Key != key)
                .Append(new(key, change(current.GetValueOrDefault(key) ?? [])))
                .Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            await OpenWithRegistry.SaveAsync(all);
        }
        finally
        {
            UpdateGate.Release();
        }
    }
}
