using Xplore.Core.Localization;
using Xplore.Core.History;
using Xplore.Core.Settings;

namespace Xplore.App;

// The app-wide undo/redo history shared by all tabs.
public sealed class ActionHistoryStore : ObservableObject
{
    private readonly ActionHistory _history;
    private readonly IUserPrompts _prompts;
    private readonly PathFormat _format;

    public ActionHistoryStore(IUserPrompts prompts, ISettings settings, PathFormat format)
    {
        _prompts = prompts;
        _format = format;
        _history = new ActionHistory(settings.Current.HistoryDepth);
        _history.Changed += () => OnPropertyChanged(nameof(Roots));
    }

    public IReadOnlyList<HistoryNodeViewModel> Roots => [Wrap(_history.Root)];

    public Func<Task> FilesChanged { get; set; } = () => Task.CompletedTask;

    public void Record(IHistoryAction action) => _history.Record(action);

    public Task UndoAsync() =>
        ReplayAsync(async () =>
        {
            await _history.UndoAsync();
            return [];
        });

    public Task RedoAsync() =>
        ReplayAsync(async () =>
        {
            await _history.RedoAsync();
            return [];
        });

    public Task ReplayToAsync(HistoryNodeViewModel target) => ReplayAsync(() => _history.ReplayToAsync(target.Node));

    private async Task ReplayAsync(Func<Task<IReadOnlyList<HistoryNode>>> step)
    {
        var failed = await step();
        await FilesChanged();
        if (failed.Count == 0)
            return;

        await _prompts.ReportErrorAsync(Loc.Format(failed.Count == 1 ? "History.Skipped.One" : "History.Skipped.Many", failed.Count, string.Join(", ", failed.Select(n => n.Action!.Name))));
    }

    private HistoryNodeViewModel Wrap(HistoryNode node) =>
        new(node, _format, node == _history.Current, node.Children.Select(Wrap).ToList());
}
