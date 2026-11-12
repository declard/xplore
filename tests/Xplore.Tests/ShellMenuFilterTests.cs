using Xplore.Core.Shell;

namespace Xplore.Tests;

public sealed class ShellMenuFilterTests
{
    private static ShellMenuItem Item(string text, string? verb = null) => new(text, 1, verb, true, false, false, IntPtr.Zero, []);

    private static ShellMenuItem Sub(string text, params ShellMenuItem[] children) => new(text, 1, null, true, false, false, IntPtr.Zero, children, true);

    private static string Texts(IEnumerable<ShellMenuItem> items) => string.Join("|", items.Select(i => i.IsSeparator ? "-" : i.Text));

    [Fact]
    public void TheApplicationsOwnCommandsAreDropped()
    {
        var items = new[] { Item("Open", "open"), Item("Cut", "cut"), ShellMenuItem.Separator, Item("Edit with Notepad++", "ANotepad++64"), Item("Properties", "properties") };

        Assert.Equal("Edit with Notepad++", Texts(ShellMenuFilter.Extras(items)));
    }

    [Fact]
    public void ASubmenuHoldingAnOwnCommandIsDropped()
    {
        var items = new[] { Sub("Open with", Item("Notepad"), Item("Choose another app", "openas")), Sub("New", Item("Folder", "NewFolder")), Sub("7-Zip", Item("Add", "SevenZipCompress")) };

        Assert.Equal("7-Zip", Texts(ShellMenuFilter.Extras(items)));
    }

    [Fact]
    public void ASubmenuWithNothingReadableIsDropped()
    {
        var items = new[] { Sub("Send to", Item("")), Sub("Empty"), Item("Share", "Windows.ModernShare") };

        Assert.Equal("Share", Texts(ShellMenuFilter.Extras(items)));
    }

    [Fact]
    public void SeparatorsAreNeverLeadingTrailingOrDoubled()
    {
        var items = new[] { ShellMenuItem.Separator, Item("A"), ShellMenuItem.Separator, Item("Cut", "cut"), ShellMenuItem.Separator, Item("B"), ShellMenuItem.Separator };

        Assert.Equal("A|-|B", Texts(ShellMenuFilter.Extras(items)));
    }

    [Theory]
    [InlineData("Open wit&h", "Open with")]
    [InlineData("Copy && paste", "Copy & paste")]
    [InlineData("Add\tCtrl+A", "Add")]
    [InlineData("a_b", "a__b")]
    public void HeadersLoseTheirMnemonics(string text, string expected) => Assert.Equal(expected, ShellMenuFilter.Header(text));
}
