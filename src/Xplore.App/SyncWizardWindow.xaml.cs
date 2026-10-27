using Xplore.Core.Localization;
using System.Windows;
using Xplore.Core.Settings;
using Xplore.Sync;
using Microsoft.Win32;

namespace Xplore.App;

public partial class SyncWizardWindow : Window
{
    private static string AllFiles => Loc.Get("Filter.AllFiles");
    private readonly SyncWizardViewModel _vm;

    public SyncWizardWindow(string left, string right, TimestampFormat time, Action? filesChanged = null)
    {
        InitializeComponent();
        _vm = new SyncWizardViewModel(left, right, time, filesChanged)
        {
            Confirm = text => MessageBox.Show(this, text, Loc.Get("Sync.RunPlan.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes,
        };
        DataContext = _vm;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _vm.Step = Math.Max(0, _vm.Step - 1);

    private void Next_Click(object sender, RoutedEventArgs e) => _vm.Step = Math.Min(3, _vm.Step + 1);

    private void CancelOperation_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

    private void Scan_Click(object sender, RoutedEventArgs e) => _vm.ScanAsync().Forget();

    private void BuildPlan_Click(object sender, RoutedEventArgs e) => _vm.BuildPlanAsync().Forget();

    private void Run_Click(object sender, RoutedEventArgs e) => _vm.RunAsync().Forget();

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((FrameworkElement)sender).Tag;
        if (tag == "ResumeLog")
        {
            BrowseResumeLog();
            return;
        }

        var current = tag switch
        {
            "Left" => _vm.Left,
            "Right" => _vm.Right,
            "PlanSource" => _vm.PlanSource,
            "PlanTarget" => _vm.PlanTarget,
            _ => _vm.QuarantineFolder,
        };
        var dialog = new OpenFolderDialog { Title = Loc.Get("Sync.Dialog.Folder"), InitialDirectory = Directory.Exists(current) ? current : null! };
        if (dialog.ShowDialog(this) != true)
            return;

        switch (tag)
        {
            case "Left":
                _vm.Left = dialog.FolderName;
                break;
            case "Right":
                _vm.Right = dialog.FolderName;
                break;
            case "PlanSource":
                _vm.PlanSource = dialog.FolderName;
                break;
            case "PlanTarget":
                _vm.PlanTarget = dialog.FolderName;
                break;
            default:
                _vm.QuarantineFolder = dialog.FolderName;
                break;
        }
    }

    private void BrowseResumeLog()
    {
        var dialog = new SaveFileDialog { Title = Loc.Get("Sync.Dialog.ResumeLog"), Filter = Loc.Get("Sync.Filter.Log") + "|" + AllFiles, OverwritePrompt = false, FileName = "xplore-sync.log" };
        if (dialog.ShowDialog(this) == true)
        {
            _vm.ResumeLog = dialog.FileName;
        }
    }

    private static string? OpenPath(Window owner, string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static (string Path, DocumentFormat Format)? SavePath(Window owner, string title, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            AddExtension = true,
            DefaultExt = extension,
            Filter = Loc.Format("Sync.Filter.Save", extension),
        };
        return dialog.ShowDialog(owner) == true ? (dialog.FileName, dialog.FilterIndex == 2 ? DocumentFormat.Yaml : DocumentFormat.Lines) : null;
    }

    private void ImportGlobs_Click(object sender, RoutedEventArgs e)
    {
        if (OpenPath(this, Loc.Get("Sync.Dialog.ImportPatterns"), Loc.Get("Sync.Filter.Text") + "|" + AllFiles) is { } path)
        {
            _vm.ImportGlobsAsync(path).Forget();
        }
    }

    private void ImportDiff_Click(object sender, RoutedEventArgs e)
    {
        if (OpenPath(this, Loc.Get("Sync.Dialog.ImportDiff"), Loc.Get("Sync.Filter.Diff") + "|" + AllFiles) is { } path)
        {
            _vm.ImportDiffAsync(path).Forget();
        }
    }

    private void ExportDiff_Click(object sender, RoutedEventArgs e)
    {
        if (SavePath(this, Loc.Get("Sync.Dialog.ExportDiff"), "xdiff") is var (path, format))
        {
            _vm.ExportDiffAsync(path, format).Forget();
        }
    }

    private void ImportPlan_Click(object sender, RoutedEventArgs e)
    {
        if (OpenPath(this, Loc.Get("Sync.Dialog.ImportPlan"), Loc.Get("Sync.Filter.Plan") + "|" + AllFiles) is { } path)
        {
            _vm.ImportPlanAsync(path).Forget();
        }
    }

    private void ExportPlan_Click(object sender, RoutedEventArgs e)
    {
        if (SavePath(this, Loc.Get("Sync.Dialog.ExportPlan"), "xplan") is var (path, format))
        {
            _vm.ExportPlanAsync(path, format).Forget();
        }
    }
}
