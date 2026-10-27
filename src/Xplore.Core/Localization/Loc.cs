using System.Globalization;
using Xplore.Core.Settings;
using YamlDotNet.Core;

namespace Xplore.Core.Localization;

// User-visible text from languages/en.yaml (the fallback) overlaid with languages/<culture>.yaml, both flat key → text maps.
// A key missing from every file shows as the key itself.
public static class Loc
{
    private const string Fallback = "en";

    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "languages");

    private static readonly IReadOnlyDictionary<string, string> English = Read(Fallback);

    private static IReadOnlyDictionary<string, string> _translation = Translate(CultureInfo.CurrentUICulture);

    public static CultureInfo? Culture { get; private set; }

    // The culture names of the language files next to the executable, English included.
    public static IReadOnlyList<string> Available { get; } =
        Directory.Exists(Folder)
            ? [.. Directory.EnumerateFiles(Folder, "*.yaml").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()]
            : [Fallback];

    public static string Get(string key) =>
        _translation.TryGetValue(key, out var text) || English.TryGetValue(key, out text) ? text : key;

    // The text of an enum member, from the key Enum.<type>.<member>.
    public static string Label<T>(T value) where T : struct, Enum => Get($"Enum.{typeof(T).Name}.{value}");

    public static string Format(string key, params object[] args) =>
        string.Format(Culture ?? CultureInfo.CurrentUICulture, Get(key), args);

    // Selects the UI language by culture name ("ru", "de-DE"); null, empty or an unknown name keeps the operating system's language.
    public static void Apply(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return;

        try
        {
            Select(CultureInfo.GetCultureInfo(language.Trim()));
        }
        catch (CultureNotFoundException)
        {
            // fall back to the default
        }
    }

    // Also makes the framework and the shell use the language.
    internal static void Select(CultureInfo culture)
    {
        Culture = culture;
        _translation = Translate(culture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    // The file of the most specific name wins: "de-DE" before "de".
    private static IReadOnlyDictionary<string, string> Translate(CultureInfo culture) =>
        new[] { culture.Name, culture.TwoLetterISOLanguageName }
            .Where(name => name.Length > 0 && name != Fallback)
            .Select(Read)
            .FirstOrDefault(map => map.Count > 0) ?? new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> Read(string name)
    {
        try
        {
            return Yaml.Reader.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Folder, name + ".yaml"))) ?? [];
        }
        catch (Exception ex) when (ex is IOException or YamlException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }
}
