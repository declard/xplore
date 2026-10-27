using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Xplore.Core.Localization;
using Xplore.App.ViewModels;

namespace Xplore.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var viewModel = await MainViewModel.CreateAsync();
        var language = XmlLanguage.GetLanguage((Loc.Culture ?? CultureInfo.CurrentUICulture).IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }
}

