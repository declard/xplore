using System.Text;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Phone;
using Xplore.Core.Settings;
using Xplore.Core.Storage;
using Xplore.Sync;

namespace Xplore.Tests;

// The sync engine, the session and the breadcrumbs reach a phone through the same calls as a disk.
[Collection("Phone")]
public sealed class PhoneSyncTests : IDisposable
{
    private const string Backup = @"Phone:\Pixel\Internal\Backup";

    private readonly FakePhone _phone = new();
    private readonly IPhoneBackend _original = Phones.Backend;
    private readonly string _local = Directory.CreateTempSubdirectory("xp-phone-sync").FullName;

    public PhoneSyncTests()
    {
        Phones.Backend = _phone;
        _phone.Items[@"\Internal\Backup"] = null;
        File.WriteAllText(Path.Combine(_local, "a.txt"), "same");
        File.WriteAllText(Path.Combine(_local, "b.txt"), "only here");
        _phone.Items[@"\Internal\Backup\a.txt"] = Encoding.UTF8.GetBytes("same");
        _phone.Items[@"\Internal\Backup\c.txt"] = Encoding.UTF8.GetBytes("only there");
    }

    public void Dispose()
    {
        Phones.Backend = _original;
        Directory.Delete(_local, true);
    }

    private static string Summary(DiffDocument d) => string.Join(", ", d.Entries.Select(e => $"{e.Path}:{e.Status}"));

    [Fact]
    public async Task ADiskFolderIsComparedWithAPhoneFolder()
    {
        var diff = await DiffService.RunAsync(new(_local, Backup, [], new()));

        Assert.Equal("a.txt:Identical, b.txt:LeftOnly, c.txt:RightOnly", Summary(diff));
    }

    [Fact]
    public async Task APlanBringsAPhoneFolderInLineWithADiskFolder()
    {
        var diff = await DiffService.RunAsync(new(_local, Backup, [], new()));
        var plan = Planner.Build(new(diff, new(SourcePolicy.Keep, MatchedPolicy.Replace, UnmatchedPolicy.Delete)));

        var report = await PlanExecutor.RunAsync(plan, new(VerifyAfterCopy: true));

        Assert.Equal(RunStatus.Success, report.Status);
        Assert.Equal([@"\Internal\Backup\a.txt", @"\Internal\Backup\b.txt"], _phone.Items.Keys.Where(k => k.StartsWith(@"\Internal\Backup\")).Order());
        Assert.Equal("only here", Encoding.UTF8.GetString(_phone.Items[@"\Internal\Backup\b.txt"]!));
    }

    [Fact]
    public async Task AFolderIsMovedOntoAPhoneFolderWithItsContent()
    {
        Directory.CreateDirectory(Path.Combine(_local, "pics", "sub"));
        File.WriteAllText(Path.Combine(_local, "pics", "sub", "p.txt"), "x");
        var plan = new PlanDocument(_local, Backup, new(), [new(PlanCommand.Move, "pics")]);

        var report = await PlanExecutor.RunAsync(plan, new(VerifyAfterCopy: true));

        Assert.Equal((RunStatus.Success, true, false), (report.Status, _phone.Items.ContainsKey(@"\Internal\Backup\pics\sub\p.txt"), Directory.Exists(Path.Combine(_local, "pics"))));
    }

    [Fact]
    public async Task TheRecycleBinCannotQuarantineOnAPhone()
    {
        var plan = new PlanDocument(_local, Backup, new(), [new(PlanCommand.Delete, "c.txt", Side.Target)]);

        await Assert.ThrowsAsync<ArgumentException>(() => PlanExecutor.RunAsync(plan, new(QuarantineMode.Recycle)));
        Assert.True(_phone.Items.ContainsKey(@"\Internal\Backup\c.txt"));
    }

    [Fact]
    public void ASessionRestoresOnlyPhoneFoldersThatExist()
    {
        var session = Session.Of([Backup, Backup + @"\gone", @"Phone:\Pixel\Internal\Backup\a.txt"], 0);

        var start = StartTabs.Resolve(AppSettings.Default with { StartLocation = new(StartMode.Restore, null) }, session);

        Assert.Equal(new string?[] { Backup, null, null }, start.Locations);
    }

    [Fact]
    public async Task BreadcrumbsAreBuiltByTheProviderOfTheLocation()
    {
        var fs = new FileSystemService(new FileLocationProvider(), [new PhoneLocationProvider()]);

        var phone = await fs.BreadcrumbsAsync(Backup + @"\Sub");
        var disk = await fs.BreadcrumbsAsync(_local);

        Assert.Equal([@"Phone:\Pixel", @"Phone:\Pixel\Internal", Backup, Backup + @"\Sub"], phone.Select(c => c.Path));
        Assert.Equal(_local, disk[^1].Path);
    }

    [Fact]
    public void ThePhoneStoreDeclaresWhatItCannotDo()
    {
        Assert.Equal(
            (StoreCapabilities.None, StoreCapabilities.RealPath | StoreCapabilities.RecycleBin | StoreCapabilities.Timestamps),
            (Stores.For(Backup).Capabilities, Stores.For(_local).Capabilities));
    }
}
