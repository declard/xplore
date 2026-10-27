using Xplore.Sync;

namespace Xplore.Tests;

public class SyncFormatTests
{
    private static readonly DateTime T = new(2024, 3, 1, 10, 0, 0, 123, DateTimeKind.Utc);

    private static readonly DiffDocument Diff = new(
        @"D:\Active", @"E:\Backup", ["**/*", "*.xml"],
        new(HashMode.Verify, TimeSpan.FromSeconds(2), true),
        [
            new(DiffStatus.Identical, @"photos\a #1.jpg", new(EntryKind.File, T, 4120, "ab12"), new(EntryKind.File, T, 4120, "ab12")),
            new(DiffStatus.Different, "b.txt", new(EntryKind.File, T, 5), new(EntryKind.File, T.AddDays(-1), 7)),
            new(DiffStatus.LeftOnly, "new", new(EntryKind.Folder, T, 0), null),
            new(DiffStatus.RightOnly, "old.txt", null, new(EntryKind.File, T, 1)),
            new(DiffStatus.TypeConflict, "x", new(EntryKind.File, T, 1), new(EntryKind.Folder, T, 0)),
        ]);

    private static readonly PlanDocument Plan = new(
        @"D:\Active", @"E:\Backup", new(SourcePolicy.Move, MatchedPolicy.Replace, UnmatchedPolicy.Delete),
        [
            new(PlanCommand.Mkdir, "new"),
            new(PlanCommand.CopyReplace, @"docs\b #1.docx", Comment: "newer"),
            new(PlanCommand.MoveReplace, "m.txt"),
            new(PlanCommand.Noop, "a.jpg", Comment: "identical"),
            new(PlanCommand.Delete, "old.txt", Side.Target),
            new(PlanCommand.Delete, "dup.txt", Side.Source, "identical"),
        ]);

    [Fact]
    public void DiffRoundTripsThroughBothFormats()
    {
        var lines = DiffFile.Serialize(Diff, DocumentFormat.Lines);
        Assert.Equal(lines, DiffFile.Serialize(DiffFile.Parse(DiffFile.Serialize(Diff, DocumentFormat.Yaml)), DocumentFormat.Lines));
        Assert.Equal(lines, DiffFile.Serialize(DiffFile.Parse(lines), DocumentFormat.Lines));
        var yaml = DiffFile.Serialize(Diff, DocumentFormat.Yaml);
        Assert.Equal(yaml, DiffFile.Serialize(DiffFile.Parse(lines), DocumentFormat.Yaml));
    }

    [Fact]
    public void PlanRoundTripsThroughBothFormats()
    {
        var lines = PlanFile.Serialize(Plan, DocumentFormat.Lines);
        Assert.Equal(lines, PlanFile.Serialize(PlanFile.Parse(PlanFile.Serialize(Plan, DocumentFormat.Yaml)), DocumentFormat.Lines));
        Assert.Equal(lines, PlanFile.Serialize(PlanFile.Parse(lines), DocumentFormat.Lines));
    }

    [Fact]
    public void ParsedDiffKeepsValues()
    {
        var doc = DiffFile.Parse(DiffFile.Serialize(Diff, DocumentFormat.Lines));
        Assert.Equal(["**/*", "*.xml"], doc.Globs);
        Assert.Equal(HashMode.Verify, doc.Options.Hash);
        Assert.True(doc.Options.ExpandSubtrees);
        Assert.Equal(@"photos\a #1.jpg", doc.Entries[0].Path);
        Assert.Equal(T, doc.Entries[1].Left!.Modified);
        Assert.Equal("ab12", doc.Entries[0].Right!.Hash);
        Assert.Null(doc.Entries[2].Right);
    }

    [Fact]
    public void HandEditedLinesAreTolerated()
    {
        var text = DiffFile.Serialize(Diff, DocumentFormat.Lines).Replace("\n", "\n\n# a comment\n");
        Assert.Equal(Diff.Entries.Count, DiffFile.Parse(text).Entries.Count);
    }

    [Fact]
    public void MalformedDocumentsReportTheLine()
    {
        var ex = Assert.Throws<FormatException>(() => PlanFile.Parse("@xplore-plan 1\n@source a\n@target b\n@policy-source keep\n@policy-matched keep\n@policy-unmatched keep\nFLY \"x\"\n"));
        Assert.StartsWith("Line 7:", ex.Message);
        Assert.Throws<FormatException>(() => DiffFile.Parse("@xplore-diff 2\n"));
        Assert.Throws<FormatException>(() => DiffFile.Parse("kind: plan\nversion: 1\n"));
    }
}

public class SyncPlannerTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = Old.AddDays(1);

    private static SideInfo File(DateTime t, long size = 1, string? hash = null) => new(EntryKind.File, t, size, hash);

    private static DiffEntry E(DiffStatus status, string path, SideInfo? l, SideInfo? r) => new(status, path, l, r);

    private static DiffDocument Doc(params DiffEntry[] entries) => new("L", "R", ["**/*"], new(), entries);

    private static IEnumerable<string> Steps(DiffDocument doc, PlanPolicy policy, bool sourceIsLeft = true) =>
        Planner.Build(new(doc, policy, sourceIsLeft)).Steps.Select(s => $"{s.Command} {(s.Side == Side.Source ? "source " : "")}{s.Path}");

    [Fact]
    public void SourceOnlyIsCopiedAndTargetOnlyIsKeptOrDeleted()
    {
        var doc = Doc(E(DiffStatus.LeftOnly, "a", File(New), null), E(DiffStatus.RightOnly, "b", null, File(New)));
        Assert.Equal(["Copy a", "Noop b"], Steps(doc, new()));
        Assert.Equal(["Copy a", "Delete b"], Steps(doc, new(Unmatched: UnmatchedPolicy.Delete)));
    }

    [Fact]
    public void SourcePolicyMoveAndDelete()
    {
        var doc = Doc(E(DiffStatus.LeftOnly, "a", File(New), null));
        Assert.Equal(["Move a"], Steps(doc, new(SourcePolicy.Move)));
        Assert.Equal(["Copy a", "Delete source a"], Steps(doc, new(SourcePolicy.Delete)));
    }

    [Fact]
    public void DirectionSwapsRoles()
    {
        var doc = Doc(E(DiffStatus.RightOnly, "a", null, File(New)));
        var plan = Planner.Build(new(doc, new(), SourceIsLeft: false));
        Assert.Equal(("R", "L"), (plan.Source, plan.Target));
        Assert.Equal(["Copy a"], plan.Steps.Select(s => $"{s.Command} {s.Path}"));
    }

    [Theory]
    [InlineData(MatchedPolicy.Keep, "Noop f")]
    [InlineData(MatchedPolicy.Replace, "CopyReplace f")]
    [InlineData(MatchedPolicy.ReplaceIfOlder, "CopyReplace f")]
    public void DifferentWithOlderTarget(MatchedPolicy matched, string expected) =>
        Assert.Equal([expected], Steps(Doc(E(DiffStatus.Different, "f", File(New), File(Old))), new(Matched: matched)));

    [Fact]
    public void ReplaceIfOlderKeepsNewerTarget() =>
        Assert.Equal(["Noop f"], Steps(Doc(E(DiffStatus.Different, "f", File(Old), File(New))), new()));

    [Fact]
    public void ReplaceMovesWhenSourcePolicyIsMove() =>
        Assert.Equal(["MoveReplace f"], Steps(Doc(E(DiffStatus.Different, "f", File(New), File(Old))), new(SourcePolicy.Move)));

    [Fact]
    public void IdenticalSourceIsDeletedOnlyWhenHashed()
    {
        var hashed = Doc(E(DiffStatus.Identical, "f", File(New, 1, "h"), File(New, 1, "h")));
        var unhashed = Doc(E(DiffStatus.Identical, "f", File(New), File(New)));
        Assert.Equal(["Delete source f"], Steps(hashed, new(SourcePolicy.Move)));
        Assert.Equal(["Noop f"], Steps(unhashed, new(SourcePolicy.Move)));
        Assert.Equal(["Noop f"], Steps(hashed, new()));
    }

    [Fact]
    public void DeletesComeLastAndExpandedFoldersAreKept()
    {
        var folder = new SideInfo(EntryKind.Folder, New, 0);
        var doc = Doc(
            E(DiffStatus.RightOnly, "d", null, folder),
            E(DiffStatus.RightOnly, @"d\x", null, File(New)),
            E(DiffStatus.LeftOnly, "z", File(New), null));
        Assert.Equal(["Noop d", "Copy z", @"Delete d\x"], Steps(doc, new(Unmatched: UnmatchedPolicy.Delete)));
    }

    [Fact]
    public void ExpandedSourceFolderIsCreatedAndItsContentsTransferred()
    {
        var folder = new SideInfo(EntryKind.Folder, New, 0);
        var doc = Doc(E(DiffStatus.LeftOnly, "d", folder, null), E(DiffStatus.LeftOnly, @"d\x", File(New), null));
        Assert.Equal(["Mkdir d", @"Copy d\x"], Steps(doc, new()));
    }

    [Fact]
    public void TypeConflictIsNeverTouched() =>
        Assert.Equal(["Noop x"], Steps(Doc(E(DiffStatus.TypeConflict, "x", File(New), new(EntryKind.Folder, New, 0))), new(Matched: MatchedPolicy.Replace)));
}

public class SyncEngineTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("xp-sync").FullName;
    private string L => Path.Combine(_root, "L");
    private string R => Path.Combine(_root, "R");
    public void Dispose() => Directory.Delete(_root, true);

    private void Write(string side, string rel, string content, DateTime? time = null)
    {
        var path = Path.Combine(_root, side, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (time is { } t) File.SetLastWriteTimeUtc(path, t);
    }

    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public SyncEngineTests()
    {
        Write("L", "same.txt", "abc", Old);
        Write("L", "changed.txt", "new content", Old.AddDays(2));
        Write("R", "changed.txt", "old", Old);
        Write("L", @"onlyl\x.txt", "x", Old);
        Write("L", @"onlyl\deep\y.txt", "y", Old);
        Write("R", "same.txt", "abc", Old);
        Write("R", "onlyr.txt", "r", Old);
        Write("L", "touched.txt", "abc", Old);
        Write("R", "touched.txt", "abc", Old.AddDays(5));
    }

    private Task<DiffDocument> Scan(DiffOptions? options = null, params string[] globs) => DiffService.RunAsync(new(L, R, globs, options ?? new()));

    private static string Summary(DiffDocument d) => string.Join(", ", d.Entries.Select(e => $"{e.Path}:{e.Status}"));

    [Fact]
    public async Task ScanClassifiesEntriesAndCollapsesOneSidedFolders()
    {
        var diff = await Scan();
        Assert.Equal(
            "changed.txt:Different, onlyl:LeftOnly, onlyr.txt:RightOnly, same.txt:Identical, touched.txt:Identical",
            Summary(diff));
        Assert.NotNull(diff.Entries.Single(e => e.Path == "touched.txt").Left!.Hash);
        Assert.Null(diff.Entries.Single(e => e.Path == "same.txt").Left!.Hash);
    }

    [Fact]
    public async Task ExpandListsSubtreesAndVerifyHashesEqualSizes()
    {
        var diff = await Scan(new(HashMode.Verify, ExpandSubtrees: true));
        Assert.Contains(@"onlyl\deep\y.txt:LeftOnly", Summary(diff));
        Assert.NotNull(diff.Entries.Single(e => e.Path == "same.txt").Left!.Hash);
    }

    [Fact]
    public async Task GlobsLimitTheScanAndFolderGlobsListFolders()
    {
        Assert.Equal("same.txt:Identical", Summary(await Scan(null, "same.txt")));
        Assert.Equal("onlyl:LeftOnly", Summary(await Scan(new(ExpandSubtrees: true), "*/")));
    }

    [Fact]
    public async Task NestedOrMissingRootsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => DiffService.RunAsync(new(L, Path.Combine(L, "onlyl"), [], new())));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => DiffService.RunAsync(new(L, Path.Combine(_root, "nope"), [], new())));
    }

    [Fact]
    public async Task ScanPlanRunMakesTargetMatchSource()
    {
        var plan = Planner.Build(new(await Scan(), new(SourcePolicy.Keep, MatchedPolicy.ReplaceIfOlder, UnmatchedPolicy.Delete)));
        var report = await PlanExecutor.RunAsync(plan, new(VerifyAfterCopy: true));

        Assert.Equal(RunStatus.Success, report.Status);
        Assert.Equal("new content", File.ReadAllText(Path.Combine(R, "changed.txt")));
        Assert.Equal("y", File.ReadAllText(Path.Combine(R, @"onlyl\deep\y.txt")));
        Assert.False(File.Exists(Path.Combine(R, "onlyr.txt")));
        Assert.True(File.Exists(Path.Combine(L, "changed.txt")));
        Assert.All((await Scan()).Entries, e => Assert.Equal(DiffStatus.Identical, e.Status));
    }

    [Fact]
    public async Task MoveRemovesSourceAndQuarantineKeepsReplacedAndDeletedItems()
    {
        var quarantine = Path.Combine(_root, "trash");
        var plan = Planner.Build(new(await Scan(), new(SourcePolicy.Move, MatchedPolicy.Replace, UnmatchedPolicy.Delete)));
        var report = await PlanExecutor.RunAsync(plan, new(QuarantineMode.Folder, quarantine));

        Assert.Equal(RunStatus.Success, report.Status);
        Assert.False(File.Exists(Path.Combine(L, "changed.txt")));
        Assert.False(Directory.Exists(Path.Combine(L, "onlyl")));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(R, "changed.txt")));
        var kept = Directory.EnumerateFiles(quarantine, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)).Order();
        Assert.Equal(["changed.txt", "onlyr.txt", "touched.txt"], kept);
    }

    [Fact]
    public async Task DryRunChangesNothing()
    {
        var plan = Planner.Build(new(await Scan(), new(Unmatched: UnmatchedPolicy.Delete)));
        var report = await PlanExecutor.RunAsync(plan, new(DryRun: true));
        Assert.Equal(RunStatus.Success, report.Status);
        Assert.True(File.Exists(Path.Combine(R, "onlyr.txt")));
        Assert.False(File.Exists(Path.Combine(R, "onlyl", "x.txt")));
    }

    [Fact]
    public async Task StalePlanFailsStepsWithoutStoppingTheRun()
    {
        var plan = new PlanDocument(L, R, new(), [new(PlanCommand.Copy, "missing.txt"), new(PlanCommand.Copy, "same.txt"), new(PlanCommand.Copy, "onlyl")]);
        var report = await PlanExecutor.RunAsync(plan, new());
        Assert.Equal([StepOutcome.Failed, StepOutcome.Failed, StepOutcome.Done], report.Results.Select(r => r.Outcome));
        Assert.Equal(RunStatus.Partial, report.Status);

        var stopped = await PlanExecutor.RunAsync(plan, new(ContinueOnError: false));
        Assert.Single(stopped.Results);
    }

    [Fact]
    public async Task ResumeLogSkipsCompletedSteps()
    {
        var log = Path.Combine(_root, "run.log");
        var plan = new PlanDocument(L, R, new(), [new(PlanCommand.Copy, "onlyl")]);
        await PlanExecutor.RunAsync(plan, new(ResumeLogPath: log));
        Directory.Delete(Path.Combine(R, "onlyl"), true);

        var second = await PlanExecutor.RunAsync(plan, new(ResumeLogPath: log));
        Assert.Equal(StepOutcome.Skipped, second.Results.Single().Outcome);
        Assert.False(Directory.Exists(Path.Combine(R, "onlyl")));
    }

    [Fact]
    public async Task PathsEscapingTheRootAreRejected()
    {
        var plan = new PlanDocument(L, R, new(), [new(PlanCommand.Delete, @"..\L\same.txt", Side.Source)]);
        await Assert.ThrowsAsync<FormatException>(() => PlanExecutor.RunAsync(plan, new()));
        Assert.True(File.Exists(Path.Combine(L, "same.txt")));
    }
}
