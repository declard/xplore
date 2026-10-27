namespace Xplore.Core.Settings;

public sealed class SettingsService(string filePath) : ISettings
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService() : this(AppFolder.File("settings.yaml")) { }

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public string FilePath => filePath;

    public event Action<AppSettings>? Changed;

    // An unreadable file (an editor still writing it) keeps the current value; only a missing file means the defaults.
    public async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var loaded = File.Exists(filePath) ? await YamlFile.ReadAsync<AppSettings>(filePath) : AppSettings.Default;
            if (loaded is not null)
            {
                Apply(loaded);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            await YamlFile.WriteAsync(filePath, settings);
            Apply(settings);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Apply(AppSettings settings)
    {
        if (settings == Current)
            return;

        Current = settings;
        Changed?.Invoke(settings);
    }
}
