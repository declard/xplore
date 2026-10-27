using YamlDotNet.Core;

namespace Xplore.Core.Settings;

// Reads and writes one YAML config file. A missing or unreadable file reads as the default value; a failed write is ignored.
// Writes are serialized so that overlapping saves never fight over the same file.
internal static class YamlFile
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public static async Task<T?> ReadAsync<T>(string path)
    {
        try
        {
            return Yaml.Reader.Deserialize<T>(await File.ReadAllTextAsync(path));
        }
        catch (Exception ex) when (ex is IOException or YamlException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    public static async Task WriteAsync<T>(string path, T value)
    {
        await WriteGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Yaml.Writer.Serialize(value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            WriteGate.Release();
        }
    }
}
