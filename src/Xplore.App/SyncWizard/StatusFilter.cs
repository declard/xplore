using System.Windows.Media;
using Xplore.Sync;

namespace Xplore.App;

public sealed class StatusFilter(DiffStatus status, Brush brush, Action changed) : ObservableObject
{
    public DiffStatus Status { get; } = status;
    public Brush Brush { get; } = brush;
    public string Label => $"{Choice<DiffStatus>.All.First(c => c.Value == Status).Label} ({Count:N0})";

    public bool IsChecked
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;

            changed();
        }
    } = true;

    public int Count
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;

            OnPropertyChanged(nameof(Label));
        }
    }
}
