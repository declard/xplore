using Xplore.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using Xplore.Core.Operations;

namespace Xplore.App;

public partial class NewFolderWindow : Window
{
    private readonly string _dir;

    public NewFolderWindow(string dir)
    {
        InitializeComponent();
        _dir = dir;
        NameBox.Text = Path.GetFileName(FileOperations.UniquePath(dir, Loc.Get("Default.NewFolder")));
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string FolderName => NameBox.Text;

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var error = FileOperations.NameError(_dir, NameBox.Text);
        Error.Text = error ?? "";
        OkButton.IsEnabled = error is null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
