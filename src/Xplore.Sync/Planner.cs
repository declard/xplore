namespace Xplore.Sync;

// Turns a diff into an ordered plan that makes the target match the source according to the policy.
// Order: folders to create (parents first), transfers and no-ops, then deletions (children first), so nothing is deleted before everything is copied.
public static class Planner
{
    public static PlanDocument Build(PlanRequest request)
    {
        var (diff, policy, sourceIsLeft) = (request.Diff, request.Policy, request.SourceIsLeft);
        var entries = diff.Entries.Order(Comparer<DiffEntry>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path))).ToList();
        var withChildren = entries.SelectMany(e => DiffService.Ancestors(e.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mkdirs = new List<PlanStep>();
        var transfers = new List<PlanStep>();
        var deletes = new List<PlanStep>();

        foreach (var e in entries)
        {
            var (src, tgt) = sourceIsLeft ? (e.Left, e.Right) : (e.Right, e.Left);
            var expandedFolder = (src ?? tgt)!.Kind == EntryKind.Folder && withChildren.Contains(e.Path);

            switch (e.Status)
            {
                case DiffStatus.TypeConflict:
                    transfers.Add(new(PlanCommand.Noop, e.Path, Comment: "type conflict: file on one side, folder on the other"));
                    break;

                case DiffStatus.Identical:
                    Identical(e.Path, src!, tgt!, policy.Source, transfers, deletes);
                    break;

                case DiffStatus.Different:
                    Replace(e, src!, tgt!, diff.Options.Tolerance, policy, transfers, deletes);
                    break;

                case DiffStatus.LeftOnly or DiffStatus.RightOnly when src is not null:
                    if (expandedFolder)
                    {
                        mkdirs.Add(new(PlanCommand.Mkdir, e.Path));
                        break;
                    }

                    Transfer(e.Path, replace: false, policy.Source, transfers, deletes);
                    break;

                default:
                    if (policy.Unmatched == UnmatchedPolicy.Keep)
                    {
                        transfers.Add(new(PlanCommand.Noop, e.Path, Comment: "only on the target"));
                        break;
                    }

                    if (expandedFolder)
                    {
                        transfers.Add(new(PlanCommand.Noop, e.Path, Comment: "folder kept, its listed contents are deleted individually"));
                        break;
                    }

                    deletes.Add(new(PlanCommand.Delete, e.Path, Side.Target, "only on the target"));
                    break;
            }
        }

        deletes.Reverse();
        return new(sourceIsLeft ? diff.Left : diff.Right, sourceIsLeft ? diff.Right : diff.Left, policy, [.. mkdirs, .. transfers, .. deletes]);
    }

    private static void Identical(string path, SideInfo src, SideInfo tgt, SourcePolicy source, List<PlanStep> transfers, List<PlanStep> deletes)
    {
        if (src.Kind == EntryKind.Folder)
            return;

        if (source == SourcePolicy.Keep)
        {
            transfers.Add(new(PlanCommand.Noop, path, Comment: "identical"));
            return;
        }

        if (src.Hash is not null && src.Hash == tgt.Hash)
        {
            deletes.Add(new(PlanCommand.Delete, path, Side.Source, "identical to the target"));
            return;
        }

        transfers.Add(new(PlanCommand.Noop, path, Comment: "identical by size and time only, source kept"));
    }

    private static void Replace(DiffEntry e, SideInfo src, SideInfo tgt, TimeSpan tolerance, PlanPolicy policy, List<PlanStep> transfers, List<PlanStep> deletes)
    {
        switch (policy.Matched)
        {
            case MatchedPolicy.Keep:
                transfers.Add(new(PlanCommand.Noop, e.Path, Comment: "differs, target kept"));
                break;
            case MatchedPolicy.ReplaceIfOlder when tgt.Modified >= src.Modified - tolerance:
                transfers.Add(new(PlanCommand.Noop, e.Path, Comment: tgt.Modified > src.Modified + tolerance ? "differs, target is newer" : "differs, same age"));
                break;
            default:
                Transfer(e.Path, replace: true, policy.Source, transfers, deletes);
                break;
        }
    }

    private static void Transfer(string path, bool replace, SourcePolicy source, List<PlanStep> transfers, List<PlanStep> deletes)
    {
        var (move, copy) = replace ? (PlanCommand.MoveReplace, PlanCommand.CopyReplace) : (PlanCommand.Move, PlanCommand.Copy);
        transfers.Add(new(source == SourcePolicy.Move ? move : copy, path));
        if (source == SourcePolicy.Delete)
        {
            deletes.Add(new(PlanCommand.Delete, path, Side.Source, "after copying"));
        }
    }
}
