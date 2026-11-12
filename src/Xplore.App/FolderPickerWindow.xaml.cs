using System.Windows;
using Xplore.App.ViewModels;
using Xplore.Core.Model;
using Xplore.Core.Storage;

namespace Xplore.App;

// The system folder dialog only knows disks, so this picker browses the same Storage tree as the main window (phones included).
public partial class FolderPickerWindow : Window
{
    private readonly TreeNodeViewModel _root;

    private FolderPickerWindow(IFileSystemService fs, string current)
    {
        InitializeComponent();
        _root = new TreeNodeViewModel(null, "", null, fs, OnSelected) { IsExpanded = true };
        Tree.ItemsSource = _root.Children;
        PathBox.Text = current;
        Loaded += async (_, _) => await RevealAsync(current);
    }

    private string? Result { get; set; }

    public static string? Pick(Window owner, IFileSystemService fs, string current)
    {
        var window = new FolderPickerWindow(fs, current) { Owner = owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void OnSelected(TreeNodeViewModel node)
    {
        if (node.Path is { } path)
        {
            PathBox.Text = path;
        }
    }

    private async Task RevealAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var node = await _root.RevealAsync(path);
        if (node is not null)
        {
            node.IsSelected = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (!IsFolder(path))
            return;

        Result = path;
        DialogResult = true;
    }

    private static bool IsFolder(string path)
    {
        try
        {
            return path.Length > 0 && Stores.IsDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
