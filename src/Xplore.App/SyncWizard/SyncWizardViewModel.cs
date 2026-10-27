using Xplore.Core.Localization;
using Xplore.Sync;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Xplore.App;

public sealed class SyncWizardViewModel : ObservableObject
{
    private readonly Action? _filesChanged;
    private CancellationTokenSource? _cts;
    private DiffDocument? _diff;
    private PlanDocument? _plan;

    public SyncWizardViewModel(string left, string right, Action? filesChanged = null)
    {
        Left = left;
        Right = right;
        _filesChanged = filesChanged;
        Filters =
        [
            .. new (DiffStatus Status, Brush Brush)[]
            {
                (DiffStatus.Different, SyncColors.Different),
                (DiffStatus.LeftOnly, SyncColors.LeftOnly),
                (DiffStatus.RightOnly, SyncColors.RightOnly),
                (DiffStatus.TypeConflict, SyncColors.Conflict),
                (DiffStatus.Identical, SyncColors.Identical),
            }.Select(f => new StatusFilter(f.Status, f.Brush, () => DiffView.Refresh())),
        ];
    }

    // Asked before a real (non-dry) run; set by the window.
    public Func<string, bool>? Confirm { get; set; }

    public IReadOnlyList<StatusFilter> Filters { get; }
    public IReadOnlyList<Choice<HashMode>> HashModes => Choice<HashMode>.All;
    public IReadOnlyList<Choice<SourcePolicy>> SourcePolicies => Choice<SourcePolicy>.All;
    public IReadOnlyList<Choice<MatchedPolicy>> MatchedPolicies => Choice<MatchedPolicy>.All;
    public IReadOnlyList<Choice<UnmatchedPolicy>> UnmatchedPolicies => Choice<UnmatchedPolicy>.All;
    public IReadOnlyList<Choice<QuarantineMode>> QuarantineModes => Choice<QuarantineMode>.All;

    public int Step { get; set => Set(ref field, value); }
    public string Status { get; private set => Set(ref field, value); } = "";
    public bool StatusIsError { get; private set => Set(ref field, value); }
    public double ProgressValue { get; private set => Set(ref field, value); }

    public double ProgressMax
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;

            OnPropertyChanged(nameof(IsIndeterminate));
        }
    }

    public bool IsIndeterminate => ProgressMax <= 0;

    public bool IsBusy
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;

            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(CanBuildPlan));
            OnPropertyChanged(nameof(CanRun));
        }
    }

    public bool CanScan => !IsBusy;
    public bool CanBuildPlan => !IsBusy && _diff is not null;
    public bool CanRun => !IsBusy && _plan is not null;

    public string Left { get; set => Set(ref field, value); }
    public string Right { get; set => Set(ref field, value); }
    public string GlobsText { get; set => Set(ref field, value); } = "";
    public string Tolerance { get; set => Set(ref field, value); } = "2";
    public HashMode Hash { get; set => Set(ref field, value); } = HashMode.Trust;
    public bool Expand { get; set => Set(ref field, value); }

    public ICollectionView DiffView { get; private set => Set(ref field, value); } = new ListCollectionView(new List<DiffRow>());
    public string DiffSummary { get; private set => Set(ref field, value); } = "";

    public int Direction { get; set => Set(ref field, value); }
    public SourcePolicy SourcePolicy { get; set => Set(ref field, value); }
    public MatchedPolicy MatchedPolicy { get; set => Set(ref field, value); } = MatchedPolicy.ReplaceIfOlder;
    public UnmatchedPolicy UnmatchedPolicy { get; set => Set(ref field, value); }
    public string PlanSource { get; set => Set(ref field, value); } = "";
    public string PlanTarget { get; set => Set(ref field, value); } = "";
    public IReadOnlyList<PlanRow> PlanRows { get; private set => Set(ref field, value); } = [];
    public string PlanSummary { get; private set => Set(ref field, value); } = "";

    public QuarantineMode Quarantine
    {
        get;
        set
        {
            if (!Set(ref field, value))
                return;

            OnPropertyChanged(nameof(QuarantineFolderEnabled));
        }
    }

    public bool QuarantineFolderEnabled => Quarantine == QuarantineMode.Folder;
    public string QuarantineFolder { get; set => Set(ref field, value); } = "";
    public string ResumeLog { get; set => Set(ref field, value); } = "";
    public bool ContinueOnError { get; set => Set(ref field, value); } = true;
    public bool VerifyAfterCopy { get; set => Set(ref field, value); }
    public bool DryRun { get; set => Set(ref field, value); } = true;
    public IReadOnlyList<ResultRow> Results { get; private set => Set(ref field, value); } = [];
    public string RunSummary { get; private set => Set(ref field, value); } = "";

    private IReadOnlyList<string> Globs =>
        [.. GlobsText.ReplaceLineEndings("\n").Split('\n').Select(g => g.Trim()).Where(g => g is not ("" or ['#', ..]))];

    private PlanDocument? EffectivePlan => _plan is null ? null : _plan with { Source = PlanSource.Trim(), Target = PlanTarget.Trim() };

    public void Cancel() => _cts?.Cancel();

    public async Task ScanAsync() => await Guarded(async ct =>
    {
        if (!double.TryParse(Tolerance, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds) || seconds < 0)
            throw new FormatException(Loc.Get("Sync.Error.Tolerance"));

        var request = new DiffRequest(Left.Trim(), Right.Trim(), Globs, new(Hash, TimeSpan.FromSeconds(seconds), Expand));
        var progress = new Progress<ScanProgress>(p => Report(Loc.Format("Sync.Progress.Scan", Loc.Label(p.Phase), p.Done, p.Total), p.Done, p.Total));
        SetDiff(await DiffService.RunAsync(request, progress, ct));
        Step = 1;
        Info(Loc.Format("Sync.Info.Compared", _diff!.Entries.Count));
    });

    public Task BuildPlanAsync() => Guarded(ct =>
    {
        var plan = Planner.Build(new(_diff!, new(SourcePolicy, MatchedPolicy, UnmatchedPolicy), SourceIsLeft: Direction == 0));
        SetPlan(plan);
        Step = 2;
        Info(Loc.Format("Sync.Info.Planned", plan.Steps.Count));
        return Task.CompletedTask;
    });

    public async Task RunAsync()
    {
        var plan = EffectivePlan!;
        if (!DryRun && !ConfirmRun(plan))
            return;

        await Guarded(async ct =>
        {
            var options = new ExecOptions(Quarantine, Trimmed(QuarantineFolder), ContinueOnError, VerifyAfterCopy, DryRun, Trimmed(ResumeLog));
            Results = [];
            var progress = new Progress<ExecProgress>(p => Report(Loc.Format("Sync.Progress.Step", Math.Min(p.Done + 1, p.Total), p.Total, p.Current.CommandName, p.Current.Path), p.Done, p.Total));
            var report = await PlanExecutor.RunAsync(plan, options, progress, ct);

            Results = [.. report.Results.Select(r => new ResultRow(r))];
            var counts = report.Results.GroupBy(r => r.Outcome).ToDictionary(g => g.Key, g => g.Count());
            RunSummary = Loc.Format(
                "Sync.Summary",
                Loc.Label(report.Status),
                counts.GetValueOrDefault(StepOutcome.Done),
                counts.GetValueOrDefault(StepOutcome.Skipped),
                counts.GetValueOrDefault(StepOutcome.Failed))
                         + (DryRun ? Loc.Get("Sync.Summary.DryRun") : "");
            if (report.Status == RunStatus.Success)
            {
                Info(RunSummary);
            }
            else
            {
                Error(RunSummary);
            }

            if (!DryRun)
            {
                _filesChanged?.Invoke();
            }
        });
    }

    public async Task ImportGlobsAsync(string path) => await Guarded(async _ => GlobsText = await File.ReadAllTextAsync(path));

    public async Task ImportDiffAsync(string path) => await Guarded(async _ =>
    {
        var diff = await DiffFile.LoadAsync(path);
        Left = diff.Left;
        Right = diff.Right;
        GlobsText = string.Join(Environment.NewLine, diff.Globs);
        Hash = diff.Options.Hash;
        Tolerance = diff.Options.Tolerance.TotalSeconds.ToString(CultureInfo.CurrentCulture);
        Expand = diff.Options.ExpandSubtrees;
        SetDiff(diff);
        Step = 1;
        Info(Loc.Format("Sync.Info.ImportedDiff", diff.Entries.Count, Path.GetFileName(path)));
    });

    public Task ExportDiffAsync(string path, DocumentFormat format) =>
        Guarded(async _ =>
        {
            await DiffFile.SaveAsync(path, _diff!, format);
            Info(Loc.Format("Sync.Info.DiffSaved", path));
        });

    public async Task ImportPlanAsync(string path) => await Guarded(async _ =>
    {
        var plan = await PlanFile.LoadAsync(path);
        SourcePolicy = plan.Policy.Source;
        MatchedPolicy = plan.Policy.Matched;
        UnmatchedPolicy = plan.Policy.Unmatched;
        SetPlan(plan);
        Step = 2;
        var missing = new[] { plan.Source, plan.Target }.Where(r => !Directory.Exists(r)).ToList();
        if (missing.Count > 0)
        {
            Error(Loc.Format("Sync.Error.FoldersMissing", string.Join(", ", missing)));
            return;
        }

        Info(Loc.Format("Sync.Info.ImportedPlan", plan.Steps.Count, Path.GetFileName(path)));
    });

    public Task ExportPlanAsync(string path, DocumentFormat format) =>
        Guarded(async _ =>
        {
            await PlanFile.SaveAsync(path, EffectivePlan!, format);
            Info(Loc.Format("Sync.Info.PlanSaved", path));
        });

    private static string? Trimmed(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private bool ConfirmRun(PlanDocument plan)
    {
        var risky = plan.Steps.Count(s => s.Command is PlanCommand.CopyReplace or PlanCommand.MoveReplace or PlanCommand.Delete or PlanCommand.Move);
        var steps = plan.Steps.Count(s => s.Command != PlanCommand.Noop);
        return Confirm?.Invoke(Loc.Format("Sync.RunPlan.Confirm", steps, risky, plan.Source, plan.Target)) == true;
    }

    private void SetDiff(DiffDocument diff)
    {
        _diff = diff;
        var rows = diff.Entries.Select(e => new DiffRow(e)).ToList();
        var view = new ListCollectionView(rows) { Filter = o => o is DiffRow r && Filters.First(f => f.Status == r.Status).IsChecked };
        foreach (var f in Filters)
        {
            f.Count = rows.Count(r => r.Status == f.Status);
        }

        DiffView = view;
        DiffSummary = $"{diff.Left}  ↔  {diff.Right}";
        OnPropertyChanged(nameof(CanBuildPlan));
        SetPlan(null);
    }

    private void SetPlan(PlanDocument? plan)
    {
        _plan = plan;
        PlanSource = plan?.Source ?? "";
        PlanTarget = plan?.Target ?? "";
        PlanRows = [.. plan?.Steps.Select((s, i) => new PlanRow(i + 1, s)) ?? []];
        PlanSummary = plan is null ? "" : string.Join(", ", plan.Steps.GroupBy(s => s.CommandName).Select(g => $"{g.Count():N0} {g.Key}"));
        Results = [];
        RunSummary = "";
        OnPropertyChanged(nameof(CanRun));
    }

    private async Task Guarded(Func<CancellationToken, Task> work)
    {
        _cts = new();
        IsBusy = true;
        Info("");
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Info(Loc.Get("Sync.Info.Cancelled"));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error(ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressMax = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Report(string text, double done, double total)
    {
        Status = text;
        ProgressMax = total;
        ProgressValue = done;
    }

    private void Info(string text) => Show(text, isError: false);

    private void Error(string text) => Show(text, isError: true);

    private void Show(string text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }
}
