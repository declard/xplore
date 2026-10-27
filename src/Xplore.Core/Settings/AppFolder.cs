namespace Xplore.Core.Settings;

// The per-user configuration folder.
public static class AppFolder
{
    public static string Location { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Xplore");

    public static string File(string name) => Path.Combine(Location, name);
}
