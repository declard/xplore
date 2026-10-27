namespace Xplore.Core.Settings;

// Remembered "Open with" applications, stored in openWith.yaml next to the settings file: file key (see AppSettings.OpenWithKey) → applications, most recent first.
public sealed class OpenWithRegistry(string filePath)
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<OpenWithApp>>> LoadAsync() =>
        await YamlFile.ReadAsync<Dictionary<string, IReadOnlyList<OpenWithApp>>>(filePath) ?? [];

    public Task SaveAsync(IReadOnlyDictionary<string, IReadOnlyList<OpenWithApp>> apps) => YamlFile.WriteAsync(filePath, apps);
}
