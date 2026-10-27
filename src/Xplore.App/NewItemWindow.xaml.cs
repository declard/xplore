using Xplore.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using Xplore.Core.Operations;

namespace Xplore.App;

public partial class NewItemWindow : Window
{
    private readonly string _dir;

    public NewItemWindow(string dir, bool isFolder)
    {
        InitializeComponent();
        _dir = dir;
        Title = Loc.Get(isFolder ? "NewFolder.Title" : "Menu.NewFile");
        Prompt.Text = Loc.Get(isFolder ? "NewFolder.Prompt" : "NewFile.Prompt");
        NameBox.Text = Path.GetFileName(isFolder
            ? FileOperations.UniquePath(dir, Loc.Get("Default.NewFolder"))
            : FileOperations.UniquePath(dir, Loc.Get("Default.NewFile"), ".txt"));
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ItemName => NameBox.Text;

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var error = FileOperations.NameError(_dir, NameBox.Text);
        Error.Text = error ?? "";
        OkButton.IsEnabled = error is null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
