using System.Text;
using Xplore.Core.Localization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Xplore.Sync;

// Reads and writes a DiffDocument as an aligned line document (@directives plus one entry per line) or as YAML; the format is detected on read.
public static class DiffFile
{
    private const string Magic = "xplore-diff";

    public static DiffDocument Parse(string text) => text.TrimStart().StartsWith('@') ? ParseLines(text) : ParseYaml(text);

    public static string Serialize(DiffDocument doc, DocumentFormat format) => format == DocumentFormat.Lines ? ToLines(doc) : ToYaml(doc);

    public static async Task<DiffDocument> LoadAsync(string path) => Parse(await File.ReadAllTextAsync(path));

    public static Task SaveAsync(string path, DiffDocument doc, DocumentFormat format) => File.WriteAllTextAsync(path, Serialize(doc, format));

    private static string ToLines(DiffDocument doc)
    {
        var width = doc.Entries.Select(e => e.Path.Length + 2).DefaultIfEmpty(0).Max();
        var sb = new StringBuilder()
            .AppendLine($"@{Magic} 1")
            .AppendLine($"@left {doc.Left}")
            .AppendLine($"@right {doc.Right}");
        foreach (var glob in doc.Globs)
        {
            sb.AppendLine($"@glob {glob}");
        }

        sb.AppendLine($"@mode {Syntax.Name(doc.Options.Hash)}")
            .AppendLine($"@tolerance {Syntax.Seconds(doc.Options.Tolerance)}")
            .AppendLine($"@expand {doc.Options.ExpandSubtrees.ToString().ToLowerInvariant()}");
        foreach (var e in doc.Entries)
        {
            sb.AppendLine($"{e.Status.Symbol()}  {Syntax.Quote(e.Path).PadRight(width)} | {Side(e.Left)} | {Side(e.Right)}");
        }

        return sb.ToString();
    }

    private static string Side(SideInfo? s)
    {
        if (s is null)
            return "-";

        var kind = s.Kind == EntryKind.File ? 'f' : 'd';
        var hash = s.Hash is null ? "" : " " + s.Hash;
        return $"{kind} {Syntax.Stamp(s.Modified)} {s.Size}{hash}";
    }

    private static DiffDocument ParseLines(string text)
    {
        var (directives, body) = Syntax.Split(text);
        Syntax.RequireHeader(directives, Magic);
        string Value(string key) => directives.FirstOrDefault(d => d.Key == key).Value ?? throw new FormatException(Loc.Format("Sync.Error.Missing", key));
        var options = new DiffOptions(
            Syntax.Parse<HashMode>(Value("mode"), "Mode"),
            Syntax.ParseSeconds(Value("tolerance")),
            Syntax.ParseBool(Value("expand")));
        var entries = Syntax.ParseBody(body, ParseEntry);
        return new(Value("left"), Value("right"), directives.Where(d => d.Key == "glob").Select(d => d.Value).ToList(), options, entries);
    }

    private static DiffEntry ParseEntry(string line)
    {
        if (line.Split('|', StringSplitOptions.TrimEntries) is not [var headText, var left, var right])
            throw new FormatException(Loc.Get("Sync.Error.DiffLine"));

        var (head, _) = Syntax.Tokenize(headText);
        var statuses = Enum.GetValues<DiffStatus>();
        if (head is not [var symbol, var path] || symbol.Length != 1 || statuses.All(s => s.Symbol() != symbol[0]))
            throw new FormatException(Loc.Get("Sync.Error.DiffStatus"));

        return new(statuses.First(s => s.Symbol() == symbol[0]), path, ParseSide(left), ParseSide(right));
    }

    private static SideInfo? ParseSide(string text)
    {
        if (text == "-")
            return null;

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [var kind and ("f" or "d"), var stamp, var size, .. var rest] when long.TryParse(size, out var n) && rest.Length <= 1 =>
                new(kind == "f" ? EntryKind.File : EntryKind.Folder, Syntax.ParseStamp(stamp), n, rest.FirstOrDefault()),
            _ => throw new FormatException(Loc.Format("Sync.Error.Side", text)),
        };
    }

    private sealed class SideDto
    {
        public string Kind { get; set; } = "";
        public string Modified { get; set; } = "";
        public long Size { get; set; }
        public string? Hash { get; set; }
    }

    private sealed class EntryDto
    {
        public string Status { get; set; } = "";
        public string Path { get; set; } = "";
        public SideDto? Left { get; set; }
        public SideDto? Right { get; set; }
    }

    private sealed class Dto
    {
        public string Kind { get; set; } = Magic;
        public int Version { get; set; } = 1;
        public string Left { get; set; } = "";
        public string Right { get; set; } = "";
        public List<string> Globs { get; set; } = [];
        public string Mode { get; set; } = "";
        public double ToleranceSeconds { get; set; }
        public bool Expand { get; set; }
        public List<EntryDto> Entries { get; set; } = [];
    }

    private static string ToYaml(DiffDocument doc) => YamlIo.Writer.Serialize(new Dto
    {
        Left = doc.Left,
        Right = doc.Right,
        Globs = [.. doc.Globs],
        Mode = Syntax.Name(doc.Options.Hash),
        ToleranceSeconds = doc.Options.Tolerance.TotalSeconds,
        Expand = doc.Options.ExpandSubtrees,
        Entries = [.. doc.Entries.Select(e => new EntryDto { Status = Syntax.Name(e.Status), Path = e.Path, Left = ToDto(e.Left), Right = ToDto(e.Right) })],
    });

    private static SideDto? ToDto(SideInfo? s) => s is null ? null : new() { Kind = Syntax.Name(s.Kind), Modified = Syntax.Stamp(s.Modified), Size = s.Size, Hash = s.Hash };

    private static SideInfo? FromDto(SideDto? d) => d is null ? null : new(Syntax.Parse<EntryKind>(d.Kind, "Kind"), Syntax.ParseStamp(d.Modified), d.Size, d.Hash);

    private static DiffDocument ParseYaml(string text)
    {
        var dto = YamlIo.Read<Dto>(text);
        if (dto.Kind != Magic || dto.Version != 1)
            throw new FormatException(Loc.Format("Sync.Error.YamlHeader", Magic));

        var options = new DiffOptions(Syntax.Parse<HashMode>(dto.Mode, "Mode"), TimeSpan.FromSeconds(dto.ToleranceSeconds), dto.Expand);
        var entries = dto.Entries.Select(e => new DiffEntry(Syntax.Parse<DiffStatus>(e.Status, "Status"), e.Path, FromDto(e.Left), FromDto(e.Right))).ToList();
        return new(dto.Left, dto.Right, dto.Globs, options, entries);
    }
}

internal static class YamlIo
{
    public static ISerializer Writer { get; } = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .Build();

    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static T Read<T>(string text)
    {
        try
        {
            return Reader.Deserialize<T>(text) ?? throw new FormatException(Loc.Get("Sync.Error.Empty"));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }
}
