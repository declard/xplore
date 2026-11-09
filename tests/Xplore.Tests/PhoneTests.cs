using System.Text;
using Xplore.Core.History;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Navigation;
using Xplore.Core.Operations;
using Xplore.Core.Phone;

namespace Xplore.Tests;

// A device kept in memory: Items maps a path inside the device to its content, null for a folder.
internal sealed class FakePhone : IPhoneBackend
{
    public const string Device = "Pixel";

    public Dictionary<string, byte[]?> Items { get; } = new(StringComparer.OrdinalIgnoreCase) { [@"\Internal"] = null };

    public IReadOnlyList<string> Devices() => [Device];

    public IEnumerable<PhoneItem> List(string device, string inside) =>
        Items.Where(i => Parent(i.Key) == inside).Select(i => ToItem(i.Key, i.Value)).ToList();

    public PhoneItem? Find(string device, string inside) =>
        inside.Length == 0 ? new(device, true, null, null) : Items.TryGetValue(inside, out var content) ? ToItem(inside, content) : null;

    public void CreateDirectory(string device, string inside) => Items[inside] = null;

    public void Delete(string device, string inside, bool isDirectory)
    {
        foreach (var key in Items.Keys.Where(k => k.Equals(inside, StringComparison.OrdinalIgnoreCase) || k.StartsWith(inside + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Items.Remove(key);
        }
    }

    public void Rename(string device, string inside, string newName)
    {
        var target = Parent(inside) + "\\" + newName;
        foreach (var key in Items.Keys.Where(k => k.Equals(inside, StringComparison.OrdinalIgnoreCase) || k.StartsWith(inside + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Items[target + key[inside.Length..]] = Items[key];
            Items.Remove(key);
        }
    }

    public void Download(string device, string inside, Stream destination) => destination.Write(Items[inside]!);

    public void Upload(string device, string inside, Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        Items[inside] = copy.ToArray();
    }

    private static string Parent(string inside) => inside[..inside.LastIndexOf('\\')];

    private static PhoneItem ToItem(string inside, byte[]? content) =>
        new(inside[(inside.LastIndexOf('\\') + 1)..], content is null, null, content?.Length);
}

[Collection("Phone")]
public sealed class PhoneTests : IDisposable
{
    private const string Storage = @"Phone:\Pixel\Internal";

    private readonly FakePhone _phone = new();
    private readonly IPhoneBackend _original = Phones.Backend;
    private readonly string _local = Directory.CreateTempSubdirectory("xp-phone").FullName;
    private readonly FileSystemService _fs = new(new FileLocationProvider(), [new PhoneLocationProvider()]);

    public PhoneTests() => Phones.Backend = _phone;

    public void Dispose()
    {
        Phones.Backend = _original;
        Directory.Delete(_local, true);
    }

    [Theory]
    [InlineData(@"Phone:\Pixel", "Pixel", "")]
    [InlineData(@"Phone:\Pixel\", "Pixel", "")]
    [InlineData(@"Phone:\Pixel\Internal\DCIM", "Pixel", @"\Internal\DCIM")]
    public void PathsSplitIntoDeviceAndInside(string path, string device, string inside) =>
        Assert.Equal((device, inside), PhonePath.Split(path));

    [Theory]
    [InlineData(@"Phone:\Pixel\Internal\DCIM", @"Phone:\Pixel\Internal")]
    [InlineData(@"Phone:\Pixel\Internal", @"Phone:\Pixel")]
    [InlineData(@"Phone:\Pixel", null)]
    public void ParentOfAPhonePathIsTheContainingLocation(string path, string? parent) =>
        Assert.Equal(parent, PhonePath.Parent(path));

    [Fact]
    public void OnlyPhonePathsAreRecognized() =>
        Assert.Equal([true, true, false, false], [PhonePath.Is(@"Phone:\Pixel"), PhonePath.Is(@"phone:\Pixel"), PhonePath.Is(@"C:\Phone:\x"), PhonePath.Is(null)]);

    [Fact]
    public void ThePathHelpersOfTheFrameworkKeepWorkingOnPhonePaths() =>
        Assert.Equal(
            [@"Phone:\Pixel\Internal\a.txt", "a.txt", @"Phone:\Pixel\Internal"],
            [Path.Combine(Storage, "a.txt"), Path.GetFileName(Storage + @"\a.txt"), Path.GetDirectoryName(Storage + @"\a.txt")]);

    [Fact]
    public void UpGoesFromTheDeviceToStorage()
    {
        var up = NavigationHistory.Start(@"Phone:\Pixel\Internal\DCIM").Up().Up().Up();

        Assert.Null(up.Current);
    }

    [Fact]
    public void PhoneLocationsAreEditableAndStreamed()
    {
        Assert.Equal(LocationCapabilities.Write | LocationCapabilities.Subfolders | LocationCapabilities.Streaming, _fs.CapabilitiesOf(Storage));
        Assert.Equal(Storage, _fs.Canonical("  " + Storage + @"\ "));
    }

    [Fact]
    public async Task StorageListsTheConnectedDevices()
    {
        var storage = await _fs.StorageAsync();

        Assert.Contains(storage, e => e is { Path: @"Phone:\Pixel", Name: "Pixel", IsDirectory: true });
    }

    [Fact]
    public async Task ListingReturnsEntriesWithPhonePaths()
    {
        _phone.Items[@"\Internal\a.txt"] = Encoding.UTF8.GetBytes("abc");
        _phone.Items[@"\Internal\DCIM"] = null;

        var entries = (await _fs.ListAsync(Storage)).ToDictionary(e => e.Name);

        Assert.Equal(
            (@"Phone:\Pixel\Internal\a.txt", 3L, @"Phone:\Pixel\Internal\DCIM", true),
            (entries["a.txt"].Path, entries["a.txt"].Size, entries["DCIM"].Path, entries["DCIM"].IsDirectory));
    }

    [Fact]
    public void ItemsAreCreatedRenamedAndDeletedOnThePhone()
    {
        var folder = FileOperations.CreateFolder(Storage, "Music");
        var file = FileOperations.CreateFile(folder, "a.txt");
        var renamed = FileOperations.Rename(file, "b.txt");
        FileOperations.Delete([renamed]);

        Assert.Equal(
            (true, false, false, Storage + @"\Music\b.txt"),
            (FileOperations.IsDirectory(folder), FileOperations.Exists(file), FileOperations.Exists(renamed), renamed));
    }

    [Fact]
    public void NamesAreMadeUniqueOnThePhone()
    {
        FileOperations.CreateFile(Storage, "New Text Document.txt");

        Assert.Equal(Storage + @"\New Text Document (2).txt", FileOperations.CreateFile(Storage));
    }

    [Fact]
    public void AFileIsCopiedToThePhoneAndBack()
    {
        var source = Path.Combine(_local, "a.txt");
        File.WriteAllText(source, "hello");

        var onPhone = FileOperations.Transfer([source], Storage, move: false).Single();
        var back = Directory.CreateDirectory(Path.Combine(_local, "back")).FullName;
        var downloaded = FileOperations.Transfer([onPhone], back, move: false).Single();

        Assert.Equal((Storage + @"\a.txt", "hello", true), (onPhone, File.ReadAllText(downloaded), File.Exists(source)));
    }

    [Fact]
    public void AFolderIsCopiedWithItsContent()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_local, "pics", "sub")).FullName;
        File.WriteAllText(Path.Combine(folder, "p.txt"), "x");

        FileOperations.Transfer([Path.Combine(_local, "pics")], Storage, move: false);

        Assert.True(_phone.Items.ContainsKey(@"\Internal\pics\sub\p.txt"));
    }

    [Fact]
    public void MovingToThePhoneRemovesTheSource()
    {
        var source = Path.Combine(_local, "a.txt");
        File.WriteAllText(source, "hello");

        FileOperations.Transfer([source], Storage, move: true);

        Assert.Equal((false, true), (File.Exists(source), _phone.Items.ContainsKey(@"\Internal\a.txt")));
    }

    [Fact]
    public void MovingBetweenPhoneFoldersKeepsTheContent()
    {
        _phone.Items[@"\Internal\a.txt"] = Encoding.UTF8.GetBytes("abc");
        _phone.Items[@"\Internal\Docs"] = null;

        var moved = FileOperations.Transfer([Storage + @"\a.txt"], Storage + @"\Docs", move: true).Single();

        Assert.Equal((@"Phone:\Pixel\Internal\Docs\a.txt", "abc", false), (moved, Encoding.UTF8.GetString(_phone.Items[@"\Internal\Docs\a.txt"]!), _phone.Items.ContainsKey(@"\Internal\a.txt")));
    }

    [Fact]
    public void AnExistingTargetIsNotOverwritten()
    {
        var source = Path.Combine(_local, "a.txt");
        File.WriteAllText(source, "new");
        _phone.Items[@"\Internal\a.txt"] = Encoding.UTF8.GetBytes("old");

        Assert.Throws<IOException>(() => FileOperations.Transfer([source], Storage, move: false));
        Assert.Equal("old", Encoding.UTF8.GetString(_phone.Items[@"\Internal\a.txt"]!));
    }

    [Fact]
    public void ShortcutsCannotInvolveAPhone()
    {
        var source = Path.Combine(_local, "a.txt");
        File.WriteAllText(source, "x");

        Assert.Throws<IOException>(() => FileOperations.CreateShortcuts([source], Storage));
    }

    [Fact]
    public async Task ACopyOntoThePhoneCanBeUndoneAndRedone()
    {
        var source = Path.Combine(_local, "a.txt");
        File.WriteAllText(source, "hello");
        var onPhone = Storage + @"\a.txt";
        var action = new CopyAction([(source, onPhone)]);

        await action.RedoAsync();
        var copied = _phone.Items.ContainsKey(@"\Internal\a.txt");
        await action.UndoAsync();

        Assert.Equal((true, false), (copied, _phone.Items.ContainsKey(@"\Internal\a.txt")));
    }

    [Fact]
    public async Task ANewFolderOnThePhoneCanBeUndoneAndRedone()
    {
        var folder = FileOperations.CreateFolder(Storage, "Music");
        var action = new CreateAction(folder, true);

        await action.UndoAsync();
        var removed = !_phone.Items.ContainsKey(@"\Internal\Music");
        await action.RedoAsync();

        Assert.Equal((true, true), (removed, _phone.Items.ContainsKey(@"\Internal\Music")));
    }

    [Fact]
    public void AFileIsExtractedForOpening()
    {
        _phone.Items[@"\Internal\a.txt"] = Encoding.UTF8.GetBytes("abc");

        var local = Phones.Extract(Storage + @"\a.txt");

        Assert.Equal(("a.txt", "abc"), (Path.GetFileName(local), File.ReadAllText(local)));
    }
}
