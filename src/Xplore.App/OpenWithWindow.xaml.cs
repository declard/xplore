using Xplore.Core.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Xplore.Core.Settings;
using Xplore.Core.Shell;
using Microsoft.Win32;

namespace Xplore.App;

public sealed record AppChoice(OpenWithApp App, ImageSource? Icon, string Group);

public partial class OpenWithWindow : Window
{
    public OpenWithWindow(string path)
    {
        InitializeComponent();
        Header.Text = Loc.Format("OpenWith.Choose", Path.GetFileName(path));
        var choices = AssocHandlers.List(path)
            .Select(h => new AppChoice(h.App, IconCache.ForApp(h.App), Loc.Get(h.Recommended ? "OpenWith.Recommended" : "OpenWith.Other")))
            .ToList();
        var view = new ListCollectionView(choices);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppChoice.Group)));
        Apps.ItemsSource = view;
    }

    public OpenWithApp? Chosen { get; private set; }

    public bool UseSystemDialog { get; private set; }

    private void Apps_SelectionChanged(object sender, SelectionChangedEventArgs e) => OpenButton.IsEnabled = Apps.SelectedItem is AppChoice;

    private void Apps_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Apps.SelectedItem is AppChoice && ItemsControl.ContainerFromElement(Apps, (DependencyObject)e.OriginalSource) is ListBoxItem)
        {
            Open_Click(sender, e);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Chosen = (Apps.SelectedItem as AppChoice)?.App;
        DialogResult = Chosen is not null;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Loc.Get("OpenWith.BrowseTitle"), Filter = Loc.Get("OpenWith.Filter") };
        if (dialog.ShowDialog(this) != true)
            return;

        var description = FileVersionInfo.GetVersionInfo(dialog.FileName).FileDescription;
        Chosen = new OpenWithApp(string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(dialog.FileName) : description, dialog.FileName);
        DialogResult = true;
    }

    private void System_Click(object sender, RoutedEventArgs e)
    {
        UseSystemDialog = true;
        DialogResult = true;
    }
}
