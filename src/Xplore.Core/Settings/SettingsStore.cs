namespace Xplore.Core.Settings;

// Where the app opens: Default: the drive list; Restore: the tabs of the previous session; Custom: one tab at the custom folder.
public enum StartMode { Default, Restore, Custom }

// What the app opens at startup. Type: see StartMode; CustomPath: the folder for Type=Custom.
public sealed record StartLocation(StartMode Type = StartMode.Default, string? CustomPath = null)
{
    public StartLocation() : this(StartMode.Default) { }
}

// A bookmarked folder. Name: display name in the tree; Path: full folder path.
public sealed record Bookmark(string Name, string Path)
{
    public Bookmark() : this("", "") { }
}

// An application remembered for "Open with". Name: menu label; Path: executable, or the shell handler id for packaged apps; Icon: "file,index" icon location.
public sealed record OpenWithApp(string Name, string Path, string? Icon = null)
{
    public OpenWithApp() : this("", "") { }
}

// HistoryDepth: max length of the undo history path before its oldest action is dropped;
// ForwardSlashes: show and copy paths with "/" instead of "\";
// NewFolderPrompt: ask for the name of a new folder before creating it instead of creating it and renaming in place;
// StartLocation: what the app opens at startup;
// PrefixTimeoutMs: max pause in milliseconds between typed characters of one prefix search in the details view;
// Language: culture name of the UI language such as "ru", null for the language of the operating system.
public sealed record AppSettings(
    int HistoryDepth = AppSettings.DefaultHistoryDepth,
    bool ForwardSlashes = false,
    bool NewFolderPrompt = false,
    StartLocation? StartLocation = null,
    int PrefixTimeoutMs = AppSettings.DefaultPrefixTimeoutMs,
    string? Language = null)
{
    public const int DefaultHistoryDepth = 100;
    public const int DefaultPrefixTimeoutMs = 1000;

    public AppSettings() : this(DefaultHistoryDepth) { }

    public static string OpenWithKey(string path) =>
        (Path.GetExtension(path) is { Length: > 0 } ext ? ext : Path.GetFileName(path)).ToLowerInvariant();

    public static AppSettings Default { get; } = new();
}

public static class PathFormat
{
    public static bool ForwardSlashes { get; set; }

    public static string Show(string path) => ForwardSlashes ? path.Replace('\\', '/') : path;
}

public static class SettingsStore
{
    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Xplore", "settings.yaml");

    public static async Task<AppSettings> LoadAsync() => await YamlFile.ReadAsync<AppSettings>(FilePath) ?? AppSettings.Default;

    public static Task SaveAsync(AppSettings settings) => YamlFile.WriteAsync(FilePath, settings);
}
