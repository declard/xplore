using System.Text.RegularExpressions;
using Xplore.Core.Localization;
using Xplore.Core.Settings;
using Xplore.Sync;

namespace Xplore.Tests;

public partial class LocalizationTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Xplore.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    [GeneratedRegex(@"""([A-Z][A-Za-z]*(?:\.[A-Za-z]+)+)""")]
    private static partial Regex KeyLiteral();

    [GeneratedRegex(@"\{l:Loc ([\w.]+)\}")]
    private static partial Regex XamlKey();

    private static IEnumerable<string> Sources(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static IReadOnlySet<string> Defined(string file) =>
        Yaml.Reader.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)).Keys.ToHashSet();

    private static string English => Path.Combine(Root, "languages", "en.yaml");

    [Fact]
    public void EveryKeyUsedInSourceIsDefined()
    {
        var defined = Defined(English);
        var used = Sources("*.cs")
            .SelectMany(f => File.ReadLines(f).Where(l => l.Contains("Loc.")).SelectMany(l => KeyLiteral().Matches(l).Select(m => m.Groups[1].Value)))
            .Concat(Sources("*.xaml").SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)));

        Assert.All(used.Distinct(), k => Assert.Contains(k, defined));
    }

    [GeneratedRegex(@"\b(?:Content|Text|Header|ToolTip|Title)=""([^""{][^""]*)""|StringFormat='([^']*)'")]
    private static partial Regex XamlLiteral();

    [GeneratedRegex(@"&#x[0-9A-Fa-f]+;")]
    private static partial Regex Entity();

    [Fact]
    public void XamlHasNoHardCodedText()
    {
        var literals = Sources("*.xaml")
            .SelectMany(f => XamlLiteral().Matches(File.ReadAllText(f)).Select(m => Entity().Replace(m.Groups[1].Value + m.Groups[2].Value, "")))
            .Where(text => text.Any(char.IsLetter) && text != "Xplore");

        Assert.Empty(literals);
    }

    [Fact]
    public void EveryEnumMemberShownToTheUserHasALabel()
    {
        var defined = Defined(English);
        var types = new[] { typeof(DiffStatus), typeof(HashMode), typeof(SourcePolicy), typeof(MatchedPolicy), typeof(UnmatchedPolicy), typeof(QuarantineMode), typeof(ScanPhase), typeof(RunStatus), typeof(StepOutcome), typeof(StartMode) };
        var keys = types.SelectMany(t => Enum.GetNames(t).Select(n => $"Enum.{t.Name}.{n}"));

        Assert.All(keys, k => Assert.Contains(k, defined));
    }

    [Fact]
    public void TranslationsDefineOnlyKnownKeys()
    {
        var english = Defined(English);
        var translations = Directory.EnumerateFiles(Path.GetDirectoryName(English)!, "*.yaml").Where(f => f != English);

        Assert.All(translations, f => Assert.All(Defined(f), k => Assert.Contains(k, english)));
    }

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<string> Translations() =>
        Directory.EnumerateFiles(Path.GetDirectoryName(English)!, "*.yaml").Where(f => f != English);

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Order().Distinct();

    [Fact]
    public void TranslationsKeepThePlaceholdersOfEnglish()
    {
        var english = Yaml.Reader.Deserialize<Dictionary<string, string>>(File.ReadAllText(English));

        Assert.All(Translations(), f => Assert.All(
            Yaml.Reader.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)),
            p => Assert.True(Placeholders(english[p.Key]).SequenceEqual(Placeholders(p.Value)), $"{Path.GetFileName(f)}: {p.Key}")));
    }

    [Fact]
    public void TranslationsAreComplete()
    {
        var english = Defined(English);

        Assert.All(Translations(), f => Assert.Equal(english.Count, Defined(f).Count));
    }

    [Fact]
    public void LanguageFilesAreCopiedNextToTheBinaries()
    {
        Assert.True(File.Exists(Path.Combine(Loc.Folder, "en.yaml")));
    }

    [Fact]
    public void EnglishTextFormatsArguments()
    {
        Assert.Equal("Moved 3 files", Loc.Format("History.Label.Many", Loc.Get("Verb.Moved"), 3, Loc.Get("Noun.Files")));
        Assert.Equal("Missing.Key", Loc.Get("Missing.Key"));
    }

    [Fact]
    public void AvailableListsTheLanguageFiles()
    {
        Assert.Contains("en", Loc.Available);
        Assert.Equal(Translations().Select(Path.GetFileNameWithoutExtension).Append("en").Order(), Loc.Available);
    }

    [Fact]
    public void ApplyIgnoresUnknownLanguages()
    {
        var before = Loc.Culture;

        Loc.Apply("not a culture");

        Assert.Equal(before, Loc.Culture);
    }
}
