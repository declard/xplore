using Xplore.Core.Settings;

namespace Xplore.Tests;

internal sealed class FakeSettings(AppSettings current) : ISettings
{
    public AppSettings Current { get; set; } = current;
    public string FilePath => "";
    public event Action<AppSettings>? Changed;
    public Task ReloadAsync() => Task.CompletedTask;
    public Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Changed?.Invoke(settings);
        return Task.CompletedTask;
    }
}

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-settings").FullName;
    private readonly List<AppSettings> _changes = [];

    private string File => Path.Combine(_dir, "settings.yaml");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsService Create()
    {
        var service = new SettingsService(File);
        service.Changed += _changes.Add;
        return service;
    }

    [Fact]
    public async Task MissingFileMeansDefaultsWithoutAChange()
    {
        var service = Create();
        await service.ReloadAsync();

        Assert.Equal(AppSettings.Default, service.Current);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task SaveUpdatesCurrentWritesTheFileAndNotifies()
    {
        var service = Create();
        var saved = AppSettings.Default with { FoldersFirst = false, PrefixTimeoutMs = 250 };
        await service.SaveAsync(saved);

        var reloaded = new SettingsService(File);
        await reloaded.ReloadAsync();
        Assert.Equal([saved], _changes);
        Assert.Equal(saved, service.Current);
        Assert.Equal(saved, reloaded.Current);
    }

    [Fact]
    public async Task SavingTheSameValueDoesNotNotify()
    {
        var service = Create();
        await service.SaveAsync(AppSettings.Default);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task ReloadPicksUpAnExternalEdit()
    {
        var service = Create();
        await System.IO.File.WriteAllTextAsync(File, "NewFolderPrompt: true\n");
        await service.ReloadAsync();

        Assert.True(service.Current.NewFolderPrompt);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task UnreadableFileKeepsTheCurrentValue()
    {
        var service = Create();
        await service.SaveAsync(AppSettings.Default with { HistoryDepth = 7 });
        await System.IO.File.WriteAllTextAsync(File, "HistoryDepth: [unclosed\n");
        await service.ReloadAsync();

        Assert.Equal(7, service.Current.HistoryDepth);
    }

    [Fact]
    public void PathFormatFollowsTheCurrentSettings()
    {
        var settings = new FakeSettings(AppSettings.Default);
        var format = new PathFormat(settings);
        Assert.Equal(@"C:\a\b", format.Show(@"C:\a\b"));

        settings.Current = AppSettings.Default with { ForwardSlashes = true };
        Assert.Equal("C:/a/b", format.Show(@"C:\a\b"));
    }
}
