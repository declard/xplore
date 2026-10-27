using Xplore.Core.Settings;

namespace Xplore.App;

public sealed class OpenWithStore(OpenWithRegistry registry)
{
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    public async Task<IReadOnlyList<OpenWithApp>> ForAsync(string path) =>
        (await registry.LoadAsync()).GetValueOrDefault(AppSettings.OpenWithKey(path)) ?? [];

    public Task RememberAsync(string path, OpenWithApp app) =>
        UpdateAsync(path, apps => apps.Where(a => !Same(a, app)).Prepend(app).ToList());

    public Task ForgetAsync(string path, OpenWithApp app) =>
        UpdateAsync(path, apps => apps.Where(a => !Same(a, app)).ToList());

    private static bool Same(OpenWithApp a, OpenWithApp b) => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

    private async Task UpdateAsync(string path, Func<IReadOnlyList<OpenWithApp>, IReadOnlyList<OpenWithApp>> change)
    {
        await _updateGate.WaitAsync();
        try
        {
            var current = await registry.LoadAsync();
            var key = AppSettings.OpenWithKey(path);
            var all = current
                .Where(kv => kv.Key != key)
                .Append(new(key, change(current.GetValueOrDefault(key) ?? [])))
                .Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            await registry.SaveAsync(all);
        }
        finally
        {
            _updateGate.Release();
        }
    }
}
