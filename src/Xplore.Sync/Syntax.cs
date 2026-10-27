using System.Globalization;
using Xplore.Core.Localization;
using System.Text;
using System.Text.RegularExpressions;

namespace Xplore.Sync;

public enum DocumentFormat { Lines, Yaml }

// Shared vocabulary of the document formats: kebab-case enum names, UTC stamps and the line grammar.
internal static partial class Syntax
{
    private const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string Name<T>(T value) where T : struct, Enum => Kebab().Replace(value.ToString(), "-$1").ToLowerInvariant();

    public static T Parse<T>(string name, string what) where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => Name(v!.Value).Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new FormatException(Loc.Format("Sync.Error.Unknown", Loc.Get("Sync.What." + what), name));

    public static string Stamp(DateTime utc) => utc.ToString(StampFormat, CultureInfo.InvariantCulture);

    public static DateTime ParseStamp(string text) =>
        DateTime.TryParseExact(text, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : throw new FormatException(Loc.Format("Sync.Error.Timestamp", text));

    public static DateTime Truncate(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    public static string Seconds(TimeSpan t) => t.TotalSeconds.ToString(CultureInfo.InvariantCulture);

    public static TimeSpan ParseSeconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s >= 0
            ? TimeSpan.FromSeconds(s)
            : throw new FormatException(Loc.Format("Sync.Error.Seconds", text));

    public static bool ParseBool(string text) =>
        bool.TryParse(text, out var b) ? b : throw new FormatException(Loc.Format("Sync.Error.Boolean", text));

    public static void RequireHeader(IReadOnlyList<(string Key, string Value)> directives, string magic)
    {
        if (directives is not [(var key, var version), ..] || key != magic || version != "1")
            throw new FormatException(Loc.Format("Sync.Error.Header", magic));
    }

    // Splits a line document into '@' directives and body lines (blank and '#' lines dropped), keeping 1-based line numbers.
    public static (List<(string Key, string Value)> Directives, List<(int Line, string Text)> Body) Split(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select((t, i) => (Line: i + 1, Text: t.Trim())).Where(l => l.Text is not ("" or ['#', ..])).ToList();
        var directives = lines.Where(l => l.Text[0] == '@').Select(l => l.Text[1..].Split([' ', '\t'], 2, StringSplitOptions.TrimEntries)).Select(p => (p[0], p.ElementAtOrDefault(1) ?? "")).ToList();
        return (directives, lines.Where(l => l.Text[0] != '@').ToList());
    }

    public static List<T> ParseBody<T>(IEnumerable<(int Line, string Text)> body, Func<string, T> parse) =>
        [.. body.Select(l => ParseLine(l, parse))];

    public static string Quote(string path) => $"\"{path}\"";

    // Reads one token at a time: bare words, "quoted paths" and a trailing '# comment'.
    public static (List<string> Tokens, string? Comment) Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;

        void Flush()
        {
            tokens.Add(sb.ToString());
            sb.Clear();
        }

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
                if (!quoted)
                {
                    Flush();
                }

                continue;
            }

            if (quoted)
            {
                sb.Append(c);
                continue;
            }

            if (c == '#')
                return (tokens, line[(i + 1)..].Trim());

            if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
                continue;
            }

            if (sb.Length > 0)
            {
                Flush();
            }
        }

        if (quoted)
            throw new FormatException(Loc.Get("Sync.Error.Quote"));

        if (sb.Length > 0)
        {
            Flush();
        }

        return (tokens, null);
    }

    private static T ParseLine<T>((int Line, string Text) line, Func<string, T> parse)
    {
        try
        {
            return parse(line.Text);
        }
        catch (FormatException ex)
        {
            throw new FormatException(Loc.Format("Sync.Error.Line", line.Line, ex.Message), ex);
        }
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex Kebab();
}
