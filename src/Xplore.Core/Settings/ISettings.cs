namespace Xplore.Core.Settings;

// The application settings: Current is the cached value, Changed fires (on the calling context) when a reload or a save changes it.
public interface ISettings
{
    AppSettings Current { get; }
    string FilePath { get; }
    event Action<AppSettings>? Changed;
    Task ReloadAsync();
    Task SaveAsync(AppSettings settings);
}
