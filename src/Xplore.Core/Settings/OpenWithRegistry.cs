namespace Xplore.Core.Settings;

// Remembered "Open with" applications, stored in openWith.yaml next to the settings file: file key (see AppSettings.OpenWithKey) → applications, most recent first.
public static class OpenWithRegistry
{
    public static string FilePath { get; } = AppFolder.File("openWith.yaml");

    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<OpenWithApp>>> LoadAsync() =>
        await YamlFile.ReadAsync<Dictionary<string, IReadOnlyList<OpenWithApp>>>(FilePath) ?? [];

    public static Task SaveAsync(IReadOnlyDictionary<string, IReadOnlyList<OpenWithApp>> apps) => YamlFile.WriteAsync(FilePath, apps);
}
