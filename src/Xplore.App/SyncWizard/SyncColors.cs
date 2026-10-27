using System.Windows.Media;

namespace Xplore.App;

public static class SyncColors
{
    public static readonly Brush Identical = Frozen("#FFFFFF");
    public static readonly Brush Different = Frozen("#FFF1C2");
    public static readonly Brush LeftOnly = Frozen("#DDF3E1");
    public static readonly Brush RightOnly = Frozen("#DCE9FA");
    public static readonly Brush Conflict = Frozen("#FADADD");
    public static readonly Brush Muted = Frozen("#8A8A8A");

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
