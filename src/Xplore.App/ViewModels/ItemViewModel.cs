using Xplore.Core.Localization;
using System.Windows.Media;
using Xplore.Core.Model;
using Xplore.Core.Settings;

namespace Xplore.App.ViewModels;

public sealed class ItemViewModel(FsEntry entry, TimestampFormat time) : ObservableObject
{
    public FsEntry Entry { get; } = entry;
    public ImageSource? Icon { get; } = IconCache.For(entry);

    public string Name => Entry.Name;

    public string DisplayName => !Entry.IsDirectory && IsLnk(Name) ? Path.GetFileNameWithoutExtension(Name) : Name;
    public double Opacity => Entry.IsHidden ? 0.5 : 1;
    public string TypeName => Entry.TypeName;
    public string ModifiedText => Entry.Modified is { } m ? time.Short(m) : "";
    public string SizeText => Entry.Size is { } s ? Loc.Format("Size.Kb", Math.Max(1, (s + 1023) / 1024)) : "";

    public bool IsSelected
    {
        get;
        set => Set(ref field, value);
    }

    public bool IsRenaming
    {
        get;
        set
        {
            if (value)
            {
                EditName = Name;
            }

            Set(ref field, value);
        }
    }

    public string EditName
    {
        get;
        set => Set(ref field, value);
    } = entry.Name;

    private static bool IsLnk(string name) =>
        Path.GetExtension(name).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
}
