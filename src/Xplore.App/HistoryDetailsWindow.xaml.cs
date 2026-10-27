using System.Windows;

namespace Xplore.App;

public partial class HistoryDetailsWindow : Window
{
    public HistoryDetailsWindow(HistoryNodeViewModel node)
    {
        InitializeComponent();
        Title = node.Label;
        Text.Text = node.Details;
    }
}
