using Xplore.Core.Localization;
using System.Windows;
using Xplore.App.ViewModels;

namespace Xplore.App;

public partial class DeleteConfirmWindow : Window
{
    public DeleteConfirmWindow(IReadOnlyList<ItemViewModel> items, bool permanent)
    {
        InitializeComponent();
        Message.Text = Question(items, permanent);
        List.ItemsSource = items;
        List.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Title = Loc.Get(permanent ? "Delete.Permanent.Title" : "Common.Delete");
        YesButton.IsDefault = !permanent;
        NoButton.IsDefault = permanent;
        Loaded += (_, _) => (permanent ? NoButton : YesButton).Focus();
    }

    private static string Question(IReadOnlyList<ItemViewModel> items, bool permanent) =>
        (items, permanent) switch
        {
            ([var single], true) => Loc.Format("Delete.Confirm.PermanentOne", single.Name),
            ([var single], false) => Loc.Format("Delete.Confirm.RecycleOne", single.Name),
            (_, true) => Loc.Format("Delete.Confirm.PermanentMany", items.Count),
            _ => Loc.Format("Delete.Confirm.RecycleMany", items.Count),
        };

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
