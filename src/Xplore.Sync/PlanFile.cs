using System.Text;
using Xplore.Core.Localization;

namespace Xplore.Sync;

// Reads and writes a PlanDocument as a line document (@directives plus one command per line) or as YAML; the format is detected on read.
public static class PlanFile
{
    private const string Magic = "xplore-plan";

    public static PlanDocument Parse(string text) => text.TrimStart().StartsWith('@') ? ParseLines(text) : ParseYaml(text);

    public static string Serialize(PlanDocument doc, DocumentFormat format) => format == DocumentFormat.Lines ? ToLines(doc) : ToYaml(doc);

    public static async Task<PlanDocument> LoadAsync(string path) => Parse(await File.ReadAllTextAsync(path));

    public static Task SaveAsync(string path, PlanDocument doc, DocumentFormat format) => File.WriteAllTextAsync(path, Serialize(doc, format));

    private static string ToLines(PlanDocument doc)
    {
        var sb = new StringBuilder()
            .AppendLine($"@{Magic} 1")
            .AppendLine($"@source {doc.Source}")
            .AppendLine($"@target {doc.Target}")
            .AppendLine($"@policy-source {Syntax.Name(doc.Policy.Source)}")
            .AppendLine($"@policy-matched {Syntax.Name(doc.Policy.Matched)}")
            .AppendLine($"@policy-unmatched {Syntax.Name(doc.Policy.Unmatched)}");
        foreach (var s in doc.Steps)
        {
            var comment = s.Comment is null ? "" : $"  # {s.Comment}";
            sb.AppendLine($"{s.CommandName} {Syntax.Quote(s.Path)}{comment}");
        }

        return sb.ToString();
    }

    private static PlanDocument ParseLines(string text)
    {
        var (directives, body) = Syntax.Split(text);
        Syntax.RequireHeader(directives, Magic);
        string Value(string key) => directives.FirstOrDefault(d => d.Key == key).Value ?? throw new FormatException(Loc.Format("Sync.Error.Missing", key));
        var policy = new PlanPolicy(
            Syntax.Parse<SourcePolicy>(Value("policy-source"), "SourcePolicy"),
            Syntax.Parse<MatchedPolicy>(Value("policy-matched"), "MatchedPolicy"),
            Syntax.Parse<UnmatchedPolicy>(Value("policy-unmatched"), "UnmatchedPolicy"));
        var steps = Syntax.ParseBody(body, ParseStep);
        return new(Value("source"), Value("target"), policy, steps);
    }

    private static PlanStep ParseStep(string line)
    {
        var (tokens, comment) = Syntax.Tokenize(line);
        return tokens switch
        {
            [var command, "source", var path] when Syntax.Parse<PlanCommand>(command, "Command") == PlanCommand.Delete => new(PlanCommand.Delete, path, Side.Source, comment),
            [var command, var path] => new(Syntax.Parse<PlanCommand>(command, "Command"), path, Side.Target, comment),
            _ => throw new FormatException(Loc.Get("Sync.Error.PlanLine")),
        };
    }

    private sealed class StepDto
    {
        public string Command { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Side { get; set; }
        public string? Comment { get; set; }
    }

    private sealed class Dto
    {
        public string Kind { get; set; } = Magic;
        public int Version { get; set; } = 1;
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public string PolicySource { get; set; } = "";
        public string PolicyMatched { get; set; } = "";
        public string PolicyUnmatched { get; set; } = "";
        public List<StepDto> Steps { get; set; } = [];
    }

    private static string ToYaml(PlanDocument doc) => YamlIo.Writer.Serialize(new Dto
    {
        Source = doc.Source,
        Target = doc.Target,
        PolicySource = Syntax.Name(doc.Policy.Source),
        PolicyMatched = Syntax.Name(doc.Policy.Matched),
        PolicyUnmatched = Syntax.Name(doc.Policy.Unmatched),
        Steps = [.. doc.Steps.Select(s => new StepDto
        {
            Command = Syntax.Name(s.Command),
            Path = s.Path,
            Comment = s.Comment,
            Side = s is { Command: PlanCommand.Delete, Side: Side.Source } ? "source" : null,
        })],
    });

    private static PlanDocument ParseYaml(string text)
    {
        var dto = YamlIo.Read<Dto>(text);
        if (dto.Kind != Magic || dto.Version != 1)
            throw new FormatException(Loc.Format("Sync.Error.YamlHeader", Magic));

        var policy = new PlanPolicy(
            Syntax.Parse<SourcePolicy>(dto.PolicySource, "SourcePolicy"),
            Syntax.Parse<MatchedPolicy>(dto.PolicyMatched, "MatchedPolicy"),
            Syntax.Parse<UnmatchedPolicy>(dto.PolicyUnmatched, "UnmatchedPolicy"));
        var steps = dto.Steps.Select(s => new PlanStep(
            Syntax.Parse<PlanCommand>(s.Command, "Command"), s.Path, s.Side is "source" ? Side.Source : Side.Target, s.Comment)).ToList();
        return new(dto.Source, dto.Target, policy, steps);
    }
}
