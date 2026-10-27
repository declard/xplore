namespace Xplore.Sync;

public enum EntryKind { File, Folder }

public enum DiffStatus { Identical, Different, LeftOnly, RightOnly, TypeConflict }

public static class DiffStatusExtensions
{
    public static char Symbol(this DiffStatus status) => status switch
    {
        DiffStatus.Identical => '=',
        DiffStatus.Different => '!',
        DiffStatus.LeftOnly => '<',
        DiffStatus.RightOnly => '>',
        _ => '~',
    };
}

public enum HashMode
{
    // Equal size and modification time (within the tolerance) count as identical without hashing.
    Trust,
    // Every pair of equal-sized files is hashed.
    Verify,
}

// One side of a compared entry. Modified is UTC, truncated to milliseconds; Hash is hex SHA-256 when computed.
public sealed record SideInfo(EntryKind Kind, DateTime Modified, long Size, string? Hash = null);

// A path relative to both roots with its state on each side (null = absent).
public sealed record DiffEntry(DiffStatus Status, string Path, SideInfo? Left, SideInfo? Right);

// ExpandSubtrees lists every entry inside a folder present on one side only instead of the folder alone.
public sealed record DiffOptions(HashMode Hash = HashMode.Trust, TimeSpan? MtimeTolerance = null, bool ExpandSubtrees = false)
{
    public TimeSpan Tolerance => MtimeTolerance ?? TimeSpan.FromSeconds(2);
}

public sealed record DiffRequest(string Left, string Right, IReadOnlyList<string> Globs, DiffOptions Options);

public sealed record DiffDocument(string Left, string Right, IReadOnlyList<string> Globs, DiffOptions Options, IReadOnlyList<DiffEntry> Entries);

public enum ScanPhase { Scanning, Hashing }

public sealed record ScanProgress(ScanPhase Phase, int Done, int Total);

// What happens to source items once they are transferred (or are found identical on the target).
public enum SourcePolicy { Keep, Move, Delete }

public enum MatchedPolicy { Keep, ReplaceIfOlder, Replace }

public enum UnmatchedPolicy { Keep, Delete }

public sealed record PlanPolicy(
    SourcePolicy Source = SourcePolicy.Keep,
    MatchedPolicy Matched = MatchedPolicy.ReplaceIfOlder,
    UnmatchedPolicy Unmatched = UnmatchedPolicy.Keep);

public enum PlanCommand { Noop, Mkdir, Copy, CopyReplace, Move, MoveReplace, Delete }

public enum Side { Source, Target }

// A command on a root-relative path; Side matters for Delete only (other commands act on the target).
public sealed record PlanStep(PlanCommand Command, string Path, Side Side = Side.Target, string? Comment = null)
{
    public string CommandName => Syntax.Name(Command).ToUpperInvariant() + (Command == PlanCommand.Delete && Side == Side.Source ? " source" : "");
}

public sealed record PlanDocument(string Source, string Target, PlanPolicy Policy, IReadOnlyList<PlanStep> Steps);

public sealed record PlanRequest(DiffDocument Diff, PlanPolicy Policy, bool SourceIsLeft = true);

// Where replaced and deleted items go: None removes them permanently, Recycle uses the Recycle Bin, Folder moves them under QuarantineFolder.
public enum QuarantineMode { None, Recycle, Folder }

public sealed record ExecOptions(
    QuarantineMode Quarantine = QuarantineMode.None,
    string? QuarantineFolder = null,
    bool ContinueOnError = true,
    bool VerifyAfterCopy = false,
    bool DryRun = false,
    string? ResumeLogPath = null);

public sealed record ExecProgress(int Done, int Total, PlanStep Current);

public enum StepOutcome { Done, Skipped, Failed }

public sealed record StepResult(int Index, PlanStep Step, StepOutcome Outcome, string? Message = null);

public enum RunStatus { Success, Partial, Failed }

public sealed record RunReport(IReadOnlyList<StepResult> Results)
{
    public RunStatus Status
    {
        get
        {
            if (Results.All(r => r.Outcome != StepOutcome.Failed))
                return RunStatus.Success;

            if (Results.Any(r => r.Outcome == StepOutcome.Done))
                return RunStatus.Partial;

            return RunStatus.Failed;
        }
    }
}
