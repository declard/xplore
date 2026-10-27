namespace Xplore.Core.Settings;

// The open tabs at the last exit. Tabs: location of each tab in order ("Storage" for the drive list); Selected: index of the active tab.
public sealed class Session
{
    public const string Storage = "Storage";

    public List<string> Tabs { get; init; } = [];
    public int Selected { get; init; }

    public static Session Of(IEnumerable<string?> locations, int selected) =>
        new() { Tabs = locations.Select(l => l ?? Storage).ToList(), Selected = selected };
}

// The tabs to open at startup. Locations: folder per tab (null for the drive list); Selected: index of the active tab.
public sealed record StartTabs(IReadOnlyList<string?> Locations, int Selected)
{
    public static StartTabs Resolve(AppSettings settings, Session? session) => (settings.StartLocation ?? new()).Type switch
    {
        StartMode.Custom => new([Existing(settings.StartLocation!.CustomPath)], 0),
        StartMode.Restore when session is { Tabs.Count: > 0 } =>
            new(session.Tabs.Select(t => t == Session.Storage ? null : Existing(t)).ToList(), Math.Clamp(session.Selected, 0, session.Tabs.Count - 1)),
        _ => new([null], 0),
    };

    private static string? Existing(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
}

public static class SessionStore
{
    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(SettingsStore.FilePath)!, "session.yaml");

    public static Task<Session?> LoadAsync() => YamlFile.ReadAsync<Session>(FilePath);

    public static Task SaveAsync(Session session) => YamlFile.WriteAsync(FilePath, session);
}
