using System.Globalization;

namespace Xplore.Core.Settings;

// The custom patterns offered in the settings for the date, the long time and the short time.
public static class TimestampPatterns
{
    public static IReadOnlyList<string> Dates { get; } = ["yyyy-MM-dd"];
    public static IReadOnlyList<string> LongTimes { get; } = ["HH:mm:ss"];
    public static IReadOnlyList<string> ShortTimes { get; } = ["HH:mm"];
}

// Shows dates and times the way the settings ask for: a custom pattern, or the regional settings of the operating system when it is unset.
public sealed class TimestampFormat(ISettings settings)
{
    public string Date(DateTime t) => Apply(t, settings.Current.DateFormat, "d");

    public string ShortTime(DateTime t) => Apply(t, settings.Current.ShortTimeFormat, "t");

    public string LongTime(DateTime t) => Apply(t, settings.Current.LongTimeFormat, "T");

    public string Short(DateTime t) => $"{Date(t)} {ShortTime(t)}";

    public string Long(DateTime t) => $"{Date(t)} {LongTime(t)}";

    private static string Apply(DateTime t, string? pattern, string osFormat)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return t.ToString(osFormat, CultureInfo.CurrentCulture);

        try
        {
            return t.ToString(pattern, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return t.ToString(osFormat, CultureInfo.CurrentCulture);
        }
    }
}
