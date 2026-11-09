using Xplore.Core.Localization;
using Xplore.Core.Operations;
using Xplore.Core.Storage;

namespace Xplore.Sync;

// Runs a plan step by step on the thread pool. Completed steps are appended to the resume log, so a re-run of the same plan skips them.
public static class PlanExecutor
{
    public static async Task<RunReport> RunAsync(PlanDocument plan, ExecOptions options, IProgress<ExecProgress>? progress = null, CancellationToken ct = default)
    {
        Roots.Validate(plan.Source, plan.Target);
        Validate(plan, options);

        var finished = ReadLog(options.ResumeLogPath);
        var run = new Run(plan, options, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var results = new List<StepResult>();

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = plan.Steps[i];
            progress?.Report(new(i, plan.Steps.Count, step));

            if (finished.Contains(LogKey(step)))
            {
                results.Add(new(i, step, StepOutcome.Skipped, Loc.Get("Sync.Result.AlreadyDone")));
                continue;
            }

            try
            {
                var result = await Task.Run(() => run.Apply(i, step), ct);
                results.Add(result);
                if (result.Outcome == StepOutcome.Done && options is { ResumeLogPath: { } log, DryRun: false })
                {
                    await File.AppendAllLinesAsync(log, [LogKey(step)], ct);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new(i, step, StepOutcome.Failed, ex.Message));
                if (!options.ContinueOnError)
                    break;
            }
        }

        progress?.Report(new(plan.Steps.Count, plan.Steps.Count, plan.Steps.LastOrDefault() ?? new(PlanCommand.Noop, "")));
        return new(results);
    }

    private static void Validate(PlanDocument plan, ExecOptions options)
    {
        if (options is { Quarantine: QuarantineMode.Folder, QuarantineFolder: null or "" })
            throw new ArgumentException(Loc.Get("Sync.Error.QuarantineRequired"));

        if (options.Quarantine == QuarantineMode.Recycle && new[] { plan.Source, plan.Target }.Any(r => !Stores.Has(r, StoreCapabilities.RecycleBin)))
            throw new ArgumentException(Loc.Get("Sync.Error.RecycleUnavailable"));

        if (plan.Steps.FirstOrDefault(s => Path.IsPathRooted(s.Path) || s.Path.Split('\\', '/').Contains("..")) is { } bad)
            throw new FormatException(Loc.Format("Sync.Error.PathOutside", bad.Path));
    }

    private static string LogKey(PlanStep s) => $"{s.Command}\t{s.Side}\t{s.Path}";

    private static HashSet<string> ReadLog(string? path) =>
        path is not null && File.Exists(path) ? [.. File.ReadLines(path)] : [];

    private sealed record Run(PlanDocument Plan, ExecOptions Options, string RunId)
    {
        public StepResult Apply(int index, PlanStep step)
        {
            var src = Path.Combine(Plan.Source, step.Path);
            var tgt = Path.Combine(Plan.Target, step.Path);
            var dry = Options.DryRun;
            string? message = dry ? Loc.Get("Sync.Result.DryRun") : null;

            switch (step.Command)
            {
                case PlanCommand.Noop:
                    return new(index, step, StepOutcome.Skipped, step.Comment);

                case PlanCommand.Mkdir:
                    if (!dry)
                    {
                        Stores.CreateDirectory(tgt);
                    }

                    break;

                case PlanCommand.Copy or PlanCommand.Move:
                    RequireExists(src);
                    if (FileOperations.Exists(tgt))
                        throw new IOException(Loc.Format("Sync.Error.TargetExists", tgt));

                    if (!dry)
                    {
                        Transfer(src, tgt, step.Command == PlanCommand.Move, overwrite: false);
                    }

                    break;

                case PlanCommand.CopyReplace or PlanCommand.MoveReplace:
                    RequireExists(src);
                    if (!dry)
                    {
                        var overwrite = Options.Quarantine == QuarantineMode.None && Stores.Info(tgt) is { IsDirectory: false };
                        if (!overwrite && FileOperations.Exists(tgt))
                        {
                            Discard(tgt, Side.Target, step.Path);
                        }

                        Transfer(src, tgt, step.Command == PlanCommand.MoveReplace, overwrite);
                    }

                    break;

                case PlanCommand.Delete:
                    var path = step.Side == Side.Source ? src : tgt;
                    if (!FileOperations.Exists(path))
                        return new(index, step, StepOutcome.Skipped, Loc.Get("Sync.Result.AlreadyGone"));

                    if (!dry)
                    {
                        Discard(path, step.Side, step.Path);
                    }

                    break;
            }

            return new(index, step, StepOutcome.Done, message);
        }

        private static void RequireExists(string path)
        {
            if (FileOperations.Exists(path))
                return;

            throw new FileNotFoundException(Loc.Format("Sync.Error.SourceMissing", path));
        }

        private void Transfer(string src, string dst, bool move, bool overwrite)
        {
            var isFile = Stores.Info(src) is { IsDirectory: false };
            var expected = Options.VerifyAfterCopy && isFile ? FileHash.Of(src) : null;
            var check = isFile ? null : (Action<StoreEntry, string>)Verify;
            if (move)
            {
                Stores.Move(src, dst, overwrite, check);
            }
            else
            {
                Stores.Copy(src, dst, overwrite, check);
            }

            if (expected is not null && FileHash.Of(dst) != expected)
                throw new IOException(Loc.Format("Sync.Error.Verify", dst));
        }

        // A folder is verified file by file as it is copied; a single file against the hash taken before it moved.
        private void Verify(StoreEntry source, string target)
        {
            if (!Options.VerifyAfterCopy || FileHash.Of(source.Path) == FileHash.Of(target))
                return;

            throw new IOException(Loc.Format("Sync.Error.Verify", target));
        }

        // Removes an item the way the quarantine mode says: permanently, to the Recycle Bin, or into the quarantine folder.
        private void Discard(string path, Side side, string relative)
        {
            switch (Options.Quarantine)
            {
                case QuarantineMode.Recycle:
                    Stores.Delete(path, recycle: true);
                    break;
                case QuarantineMode.Folder:
                    var kept = Path.Combine(Options.QuarantineFolder!, RunId, side.ToString().ToLowerInvariant(), relative);
                    Transfer(path, kept, move: true, overwrite: true);
                    break;
                default:
                    Stores.Delete(path);
                    break;
            }
        }
    }
}
