using Xplore.Core.Localization;
using System.Windows.Media;
using Xplore.Core.Model;

namespace Xplore.App.ViewModels;

public sealed class ItemViewModel(FsEntry entry) : ObservableObject
{
    public FsEntry Entry { get; } = entry;
    public ImageSource? Icon { get; } = IconCache.For(entry);

    public string Name => Entry.Name;
    public string TypeName => Entry.TypeName;
    public string ModifiedText => Entry.Modified?.ToString("yyyy-MM-dd HH:mm") ?? "";
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
}
