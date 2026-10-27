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

    private static readonly DateTime Stamp = new(2026, 3, 9, 14, 5, 7);

    [Fact]
    public void TimestampFormatDefaultsToTheOperatingSystem()
    {
        var format = new TimestampFormat(new FakeSettings(AppSettings.Default));
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        Assert.Equal(Stamp.ToString("d", culture), format.Date(Stamp));
        Assert.Equal(Stamp.ToString("t", culture), format.ShortTime(Stamp));
        Assert.Equal(Stamp.ToString("T", culture), format.LongTime(Stamp));
    }

    [Fact]
    public void TimestampFormatAppliesCustomPatternsAndFollowsTheSettings()
    {
        var settings = new FakeSettings(AppSettings.Default with { DateFormat = "yyyy-MM-dd", LongTimeFormat = "HH:mm:ss", ShortTimeFormat = "HH:mm" });
        var format = new TimestampFormat(settings);

        Assert.Equal("2026-03-09 14:05", format.Short(Stamp));
        Assert.Equal("2026-03-09 14:05:07", format.Long(Stamp));

        settings.Current = AppSettings.Default with { DateFormat = "dd.MM.yyyy" };
        Assert.Equal("09.03.2026", format.Date(Stamp));
    }

    [Fact]
    public void TimestampFormatFallsBackToTheOperatingSystemOnAnInvalidPattern()
    {
        var format = new TimestampFormat(new FakeSettings(AppSettings.Default with { DateFormat = "x" }));
        Assert.Equal(Stamp.ToString("d", System.Globalization.CultureInfo.CurrentCulture), format.Date(Stamp));
    }
}
