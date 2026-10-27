using Xplore.Core.Localization;
using Xplore.Core.Settings;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Xplore.App;

public partial class SettingsWindow : Window
{
    private sealed record LanguageChoice(string Code, string Name);

    private sealed record PatternChoice(string Value, string Label);

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        var start = settings.StartLocation ?? new();
        LanguageBox.ItemsSource = LanguageChoices(settings.Language);
        LanguageBox.SelectedValue = settings.Language ?? "";
        ForwardSlashesBox.IsChecked = settings.ForwardSlashes;
        NewItemPromptBox.IsChecked = settings.NewItemPrompt;
        FoldersFirstBox.IsChecked = settings.FoldersFirst;
        Show(DateFormatBox, TimestampPatterns.Dates, settings.DateFormat);
        Show(LongTimeFormatBox, TimestampPatterns.LongTimes, settings.LongTimeFormat);
        Show(ShortTimeFormatBox, TimestampPatterns.ShortTimes, settings.ShortTimeFormat);
        HistoryDepthBox.Text = settings.HistoryDepth.ToString(CultureInfo.InvariantCulture);
        PrefixTimeoutBox.Text = settings.PrefixTimeoutMs.ToString(CultureInfo.InvariantCulture);
        StartTypeBox.ItemsSource = Choice<StartMode>.All;
        StartTypeBox.SelectedValue = start.Type;
        CustomPathBox.Text = start.CustomPath ?? "";
    }

    // Whether the user asked for the settings file instead of saving.
    public bool OpenFile { get; private set; }

    public AppSettings Result => _settings with
    {
        Language = LanguageBox.SelectedValue is string { Length: > 0 } code ? code : null,
        ForwardSlashes = ForwardSlashesBox.IsChecked == true,
        NewItemPrompt = NewItemPromptBox.IsChecked == true,
        FoldersFirst = FoldersFirstBox.IsChecked == true,
        DateFormat = Pattern(DateFormatBox),
        LongTimeFormat = Pattern(LongTimeFormatBox),
        ShortTimeFormat = Pattern(ShortTimeFormatBox),
        HistoryDepth = int.Parse(HistoryDepthBox.Text.Trim(), CultureInfo.InvariantCulture),
        PrefixTimeoutMs = int.Parse(PrefixTimeoutBox.Text.Trim(), CultureInfo.InvariantCulture),
        StartLocation = new((StartMode)StartTypeBox.SelectedValue, CustomPathBox.Text.Trim() is { Length: > 0 } path ? path : null),
    };

    private static string? Pattern(ComboBox box) => box.SelectedValue is string { Length: > 0 } pattern ? pattern : null;

    private static void Show(ComboBox box, IEnumerable<string> patterns, string? current)
    {
        IReadOnlyList<PatternChoice> choices =
        [
            new("", Loc.Get("Settings.Format.System")),
            .. patterns.Append(current).OfType<string>().Where(pattern => pattern.Length > 0).Distinct().Select(pattern => new PatternChoice(pattern, pattern)),
        ];
        box.ItemsSource = choices;
        box.SelectedValue = current ?? "";
    }

    private static IReadOnlyList<LanguageChoice> LanguageChoices(string? current) =>
    [
        new("", Loc.Get("Settings.Language.System")),
        .. Loc.Available.Append(current).OfType<string>().Where(code => code.Length > 0).Distinct().Select(Describe),
    ];

    private static LanguageChoice Describe(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return new(code, name.Length == 0 ? code : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..]);
        }
        catch (CultureNotFoundException)
        {
            return new(code, code);
        }
    }

    private static bool IsNumber(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 1;

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        var error = new[] { (Box: HistoryDepthBox, Name: "Settings.HistoryDepth"), (Box: PrefixTimeoutBox, Name: "Settings.PrefixTimeout") }
            .Where(field => !IsNumber(field.Box.Text))
            .Select(field => Loc.Format("Settings.Error.Number", Loc.Get(field.Name)))
            .FirstOrDefault();
        Error.Text = error ?? "";
        OkButton.IsEnabled = error is null;
    }

    private void StartType_Changed(object sender, SelectionChangedEventArgs e)
    {
        var custom = StartTypeBox.SelectedValue is StartMode.Custom;
        CustomPathBox.IsEnabled = custom;
        BrowseButton.IsEnabled = custom;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var current = CustomPathBox.Text.Trim();
        var dialog = new OpenFolderDialog { Title = Loc.Get("Sync.Dialog.Folder"), InitialDirectory = Directory.Exists(current) ? current : null! };
        if (dialog.ShowDialog(this) == true)
        {
            CustomPathBox.Text = dialog.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        OpenFile = true;
        DialogResult = false;
    }
}
