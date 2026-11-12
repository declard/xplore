using System.Text;

namespace Xplore.Core.Shell;

// Reduces the shell's own menu to what the application does not already offer itself: the third-party handlers (7-Zip, Git Extensions...) and registered verbs.
public static class ShellMenuFilter
{
    // Commands the application's own menus already have.
    private static readonly HashSet<string> OwnVerbs = new(
        ["open", "opennewwindow", "opennewprocess", "explore", "openas", "cut", "copy", "paste", "pastelink", "delete", "rename", "properties", "link", "copyaspath", "sendto", "find", "NewFolder", "NewLink"],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ShellMenuItem> Extras(IReadOnlyList<ShellMenuItem> items) => Tidy([.. items.Select(Strip).OfType<ShellMenuItem>()]);

    // A menu entry as WPF shows it: no mnemonic markers, no shortcut text after a tab, underscores escaped.
    public static string Header(string text)
    {
        var label = text.Split('\t')[0];
        var result = new StringBuilder();
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] != '&')
            {
                result.Append(label[i] == '_' ? "__" : label[i]);
                continue;
            }

            if (i + 1 < label.Length && label[i + 1] == '&')
            {
                result.Append('&');
                i++;
            }
        }

        return result.ToString();
    }

    private static bool IsOwn(ShellMenuItem item) =>
        item.Verb is { } verb && OwnVerbs.Contains(verb) || item.Children.Any(IsOwn);

    // A submenu holding one of the application's commands is the shell's version of it ("Open with", "New"); one with nothing readable in it (such as "Send to", which is filled by drawing) is of no use.
    private static ShellMenuItem? Strip(ShellMenuItem item)
    {
        if (item.IsSeparator)
            return item;

        if (IsOwn(item))
            return null;

        if (!item.IsSubmenu)
            return item;

        var children = Tidy([.. item.Children.Select(Strip).OfType<ShellMenuItem>()]);
        return children.Any(c => c.Text.Length > 0) ? item with { Children = children } : null;
    }

    private static IReadOnlyList<ShellMenuItem> Tidy(IReadOnlyList<ShellMenuItem> items)
    {
        var result = new List<ShellMenuItem>();
        foreach (var item in items)
        {
            if (item.IsSeparator && (result.Count == 0 || result[^1].IsSeparator))
                continue;

            result.Add(item);
        }

        if (result.Count > 0 && result[^1].IsSeparator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
